using System.ComponentModel;
using System.Text.Json.Nodes;
using Kronan.McparIs.Authentication;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kronan.McparIs.Tools;

[McpServerToolType, McpServerResourceType]
public sealed class PresentationTools(ShoppingNoteService shopping, CatalogService catalog,
    IHttpContextAccessor contexts, IOptions<AuthenticationOptions> auth, IOptions<UiOptions> ui)
{
    public const string ResourceUri = "ui://kronan/shopping-v1.html";
    private bool CanWrite => HouseholdAccess.HasScope(contexts.HttpContext!.User, auth.Value.ShoppingWriteScope);

    [Authorize(Policy = "shopping.read")]
    [McpServerTool(Name = "ShowShoppingList", ReadOnly = false, Destructive = false, Idempotent = false),
     McpMeta("ui", JsonValue = "{\"resourceUri\":\"ui://kronan/shopping-v1.html\"}"),
     Description("Show the live household shopping note as an interactive card. Fetches current state; Krónan may create a note if none exists.")]
    public async Task<CallToolResult> ShowShoppingList(CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { note = await shopping.GetAsync(cancellationToken), canWrite = CanWrite }, "Your shopping list is shown in the card.");

    [Authorize(Policy = "catalog")]
    [McpServerTool(Name = "ShowProducts", ReadOnly = true, Destructive = false, Idempotent = true),
     McpMeta("ui", JsonValue = "{\"resourceUri\":\"ui://kronan/shopping-v1.html\"}"),
     Description("Show up to six selected product SKUs as cards with authoritative prices and optional Add buttons. Use SearchProducts first to choose SKUs.")]
    public async Task<CallToolResult> ShowProducts(string[] skus, CancellationToken cancellationToken = default)
    {
        if (skus is not { Length: >= 1 and <= 6 }) throw new ServiceFailure("invalid_input", "Choose between one and six product SKUs.");
        var products = new List<Models.ProductDetail>();
        foreach (var sku in skus.Distinct(StringComparer.Ordinal))
            if (await catalog.ProductAsync(sku, cancellationToken) is { } product) products.Add(product);
        return ToolResults.Success(new { products, canWrite = CanWrite }, "Selected products are shown in the card.");
    }

    [Authorize(Policy = "member")]
    [McpServerResource(UriTemplate = ResourceUri, Name = "kronan-shopping", MimeType = "text/html;profile=mcp-app")]
    public TextResourceContents ShoppingUi()
    {
        using var stream = typeof(PresentationTools).Assembly.GetManifestResourceStream("Kronan.mcpar.is.Ui.shopping.html")
            ?? throw new InvalidOperationException("UI resource missing.");
        using var reader = new StreamReader(stream);
        return new TextResourceContents
        {
            Uri = ResourceUri, MimeType = "text/html;profile=mcp-app", Text = reader.ReadToEnd(),
            Meta = new JsonObject
            {
                ["ui"] = new JsonObject
                {
                    ["domain"] = new Uri(auth.Value.Resource).GetLeftPart(UriPartial.Authority),
                    ["prefersBorder"] = true,
                    ["csp"] = new JsonObject
                    {
                        ["connectDomains"] = new JsonArray(),
                        ["resourceDomains"] = new JsonArray(ui.Value.ImageOrigins.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())
                    }
                }
            }
        };
    }
}
