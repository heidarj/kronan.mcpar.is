using System.Security.Cryptography;
using System.Text;
using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Options;
using Kronan.McparIs.Tools;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Bind Kronan configuration (supports env var KRONAN__APIKEY or config section Kronan)
builder.Services.Configure<KronanOptions>(options =>
{
    builder.Configuration.GetSection(KronanOptions.SectionName).Bind(options);

    // Allow flat env var override: KRONAN_API_KEY
    var envApiKey = builder.Configuration["KRONAN_API_KEY"];
    if (!string.IsNullOrWhiteSpace(envApiKey))
        options.ApiKey = envApiKey;
});

builder.Services.Configure<McpServerOptions>(options =>
{
    builder.Configuration.GetSection(McpServerOptions.SectionName).Bind(options);

    var envApiKey = builder.Configuration["MCP_SERVER_API_KEY"];
    if (!string.IsNullOrWhiteSpace(envApiKey))
        options.ApiKey = envApiKey;
});

// Register the typed Kronan HTTP client
builder.Services.AddHttpClient<KronanClient>();

// Register MCP server with Streamable HTTP transport
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(KronanTools).Assembly);

var app = builder.Build();
var mcpServerOptions = app.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/mcp"))
    {
        await next();
        return;
    }

    if (string.IsNullOrWhiteSpace(mcpServerOptions.ApiKey))
    {
        if (app.Environment.IsDevelopment())
        {
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "MCP endpoint is not configured. Set MCP_SERVER_API_KEY or McpServer:ApiKey."
        });
        return;
    }

    var providedApiKey = context.Request.Headers["X-Api-Key"].ToString();
    if (!FixedTimeEquals(providedApiKey, mcpServerOptions.ApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Unauthorized"
        });
        return;
    }

    await next();
});

app.MapMcp("/mcp");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

static bool FixedTimeEquals(string left, string right)
{
    if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        return false;

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left),
        Encoding.UTF8.GetBytes(right));
}
