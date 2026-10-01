using System.ComponentModel;
using Kronan.McparIs.Services;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kronan.McparIs.Tools;

[McpServerToolType, Authorize(Policy = "catalog")]
public sealed class KronanTools(CatalogService catalog)
{
    [McpServerTool(Name = "SearchProducts", ReadOnly = true, Destructive = false, Idempotent = true),
     Description("Search the Krónan home-delivery catalogue by keyword. Prices and availability may differ in store.")]
    public async Task<CallToolResult> SearchProducts(string query, int page = 1, CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { products = await catalog.SearchAsync(query, page, cancellationToken) }, "Product search results.");

    [McpServerTool(Name = "GetProduct", ReadOnly = true, Destructive = false, Idempotent = true), Description("Get a product by its SKU.")]
    public async Task<CallToolResult> GetProduct(string sku, CancellationToken cancellationToken = default)
    {
        var product = await catalog.ProductAsync(sku, cancellationToken);
        return product is null ? ToolResults.Failure(new ServiceFailure("product_not_found", "That product was not found."))
            : ToolResults.Success(new { product }, "Product details.");
    }

    [McpServerTool(Name = "ListCategories", ReadOnly = true, Destructive = false, Idempotent = true), Description("List product categories.")]
    public async Task<CallToolResult> ListCategories(CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { categories = await catalog.CategoriesAsync(cancellationToken) }, "Product categories.");
}
