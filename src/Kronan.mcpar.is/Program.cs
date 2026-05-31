using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Options;
using Kronan.McparIs.Tools;

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

// Register the typed Kronan HTTP client
builder.Services.AddHttpClient<KronanClient>();

// Register MCP server with Streamable HTTP transport
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(KronanTools).Assembly);

var app = builder.Build();

app.MapMcp("/mcp");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
