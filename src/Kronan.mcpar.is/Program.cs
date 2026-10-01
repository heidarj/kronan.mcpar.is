using System.Threading.RateLimiting;
using System.Text.Json.Nodes;
using Kronan.McparIs.Authentication;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Kronan.McparIs.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using AuthOptions = Kronan.McparIs.Options.AuthenticationOptions;

var builder = WebApplication.CreateBuilder(args);
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
var developmentBypass = auth.DevelopmentBypass && builder.Environment.IsDevelopment();
if (auth.DevelopmentBypass && !developmentBypass) throw new InvalidOperationException("Development authentication cannot be enabled outside Development.");
if (developmentBypass)
{
    builder.WebHost.UseUrls("http://127.0.0.1:5076");
    auth.Resource = "http://127.0.0.1:5076/mcp";
    auth.AllowedSubjects = ["local-development"];
}
else if (!Https(auth.Authority) || !Https(auth.Resource) || string.IsNullOrWhiteSpace(auth.Audience) ||
    auth.AllowedSubjects.Length == 0 || auth.AllowedSubjects.Length > 10 || auth.AllowedSubjects.Any(string.IsNullOrWhiteSpace) ||
    auth.SubjectClaim is not ("sub" or "oid"))
    throw new InvalidOperationException("Configure an HTTPS OAuth Authority and Resource, the API Audience, and the household's allowed subjects (sub or oid).");
if (auth.Scopes.Any(s => string.IsNullOrWhiteSpace(s) || s.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\\')))
    throw new InvalidOperationException("OAuth scopes must be nonempty and safe to publish in metadata.");
builder.Services.AddSingleton(Options.Create(auth));
builder.Services.AddOptions<KronanOptions>().Configure(options =>
{
    builder.Configuration.GetSection(KronanOptions.SectionName).Bind(options);
    if (builder.Configuration["KRONAN_API_KEY"] is { Length: > 0 } key) options.ApiKey = key;
}).Validate(o => Https(o.BaseUrl) && o.BaseUrl.EndsWith('/'), "Krónan BaseUrl must be an HTTPS directory URL.")
  .Validate(o => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(o.ApiKey), "Production requires a Krónan access token.")
  .Validate(o => o.ApiKey is null || !o.ApiKey.Any(char.IsWhiteSpace), "The Krónan access token cannot contain whitespace.").ValidateOnStart();
builder.Services.AddOptions<LimitsOptions>().BindConfiguration(LimitsOptions.SectionName)
    .Validate(o => o.UpstreamRequests is >= 1 and <= 120 && o.PacingMilliseconds >= 0 && o.StartupCooldownSeconds >= 0 &&
        o.ToolRequestsPerMinute is >= 1 and <= 60 && o.MutationRequestsPerMinute is >= 1 and <= 20 &&
        o.OperationCapacity is >= 1 and <= 2048 && o.OperationLifetimeSeconds is >= 60 and <= 1200, "Invalid limits configuration.")
    .Validate(o => builder.Environment.IsDevelopment() || (o.StartupCooldownSeconds >= 200 && o.PacingMilliseconds >= 2000),
        "Production requires a 200-second restart cooldown and at least two seconds between upstream requests.").ValidateOnStart();
builder.Services.AddOptions<UiOptions>().BindConfiguration("Ui")
    .Validate(o => o.ImageOrigins.Length <= 20 && o.ImageOrigins.All(Https), "UI image origins must be HTTPS URLs.").ValidateOnStart();

var authentication = builder.Services.AddAuthentication(developmentBypass ? "loopback" : JwtBearerDefaults.AuthenticationScheme);
if (developmentBypass) authentication.AddScheme<AuthenticationSchemeOptions, LoopbackAuthenticationHandler>("loopback", _ => { });
else authentication.AddJwtBearer(options =>
{
    options.Authority = auth.Authority;
    options.Audience = auth.Audience;
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        RequireSignedTokens = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{auth.MetadataUrl}\"";
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("member", policy => policy.RequireAuthenticatedUser().RequireAssertion(c => HouseholdAccess.IsMember(c.User, auth)));
    foreach (var (name, scope) in new[] { ("catalog", auth.CatalogScope), ("shopping.read", auth.ShoppingReadScope), ("shopping.write", auth.ShoppingWriteScope) })
        options.AddPolicy(name, policy => policy.RequireAuthenticatedUser().RequireAssertion(c => HouseholdAccess.IsMember(c.User, auth) && HouseholdAccess.HasScope(c.User, scope)));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RequestBudget>();
builder.Services.AddSingleton<MutationCoordinator>();
builder.Services.AddSingleton<CatalogCache>();
builder.Services.AddMemoryCache(o => o.SizeLimit = 256);
builder.Services.AddScoped<HouseholdAccess>();
builder.Services.AddScoped<ShoppingNoteService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddHttpClient<KronanClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddRequestTimeouts(o => o.DefaultPolicy = new() { Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("transport", context =>
    {
        var member = HouseholdAccess.IsMember(context.User, auth);
        var key = member ? context.User.FindFirst(auth.SubjectClaim)!.Value : "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = member ? 120 : 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        });
    });
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddMcpServer().WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly(typeof(KronanTools).Assembly).WithResources<PresentationTools>().AddAuthorizationFilters()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => async (context, ct) =>
        {
            var services = context.Services!;
            try
            {
                var actor = services.GetRequiredService<HouseholdAccess>().RequireMember();
                services.GetRequiredService<RequestBudget>().Tool(actor.Subject);
                return await next(context, ct);
            }
            catch (ServiceFailure failure) { return ToolResults.Failure(failure); }
            catch (OperationCanceledException) { return ToolResults.Failure(new ServiceFailure("cancelled", "The request was cancelled. Check the list before repeating a change.")); }
            catch (Exception ex)
            {
                services.GetRequiredService<ILogger<Program>>().LogError("Tool failed with exception type {ExceptionType}", ex.GetType().Name);
                return ToolResults.Failure(new ServiceFailure("tool_failed", "The operation could not be completed. Check the list before repeating a change."));
            }
        });
        filters.AddListToolsFilter(next => async (context, ct) =>
        {
            var result = await next(context, ct);
            foreach (var tool in result.Tools)
            {
                var scope = tool.Name switch
                {
                    "GetShoppingList" or "ShowShoppingList" => auth.ShoppingReadScope,
                    "PrepareShoppingChange" or "AddShoppingItems" or "UpdateShoppingItem" or "RemoveShoppingItem" => auth.ShoppingWriteScope,
                    _ => auth.CatalogScope
                };
                tool.Meta ??= new JsonObject();
                tool.Meta["securitySchemes"] = new JsonArray(new JsonObject { ["type"] = "oauth2", ["scopes"] = new JsonArray(scope) });
                // Both model and component calls use the same registered tools and policies.
                if (tool.Meta["ui"] is not JsonObject) tool.Meta["ui"] = new JsonObject();
                tool.Meta["ui"]!["visibility"] = new JsonArray("model", "app");
            }
            return result;
        });
    });

var app = builder.Build();
// Construct the singleton at startup, so cooldown starts at process startup rather than first use.
var budget = app.Services.GetRequiredService<RequestBudget>();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseRequestTimeouts();
app.MapMcp("/mcp").RequireAuthorization("member").RequireRateLimiting("transport");
var metadata = new { resource = auth.Resource, authorization_servers = new[] { auth.Authority }, scopes_supported = auth.Scopes, bearer_methods_supported = new[] { "header" } };
app.MapGet("/.well-known/oauth-protected-resource", () => Results.Json(metadata)).RequireRateLimiting("transport");
app.MapGet("/.well-known/oauth-protected-resource/mcp", () => Results.Json(metadata)).RequireRateLimiting("transport");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/ready", () => budget.StartupRetrySeconds == 0 ? Results.Ok(new { status = "ready" }) :
    Results.Json(new { status = "warming_up", retryAfterSeconds = budget.StartupRetrySeconds }, statusCode: 503));
app.Run();

static bool Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
    string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

public partial class Program;
