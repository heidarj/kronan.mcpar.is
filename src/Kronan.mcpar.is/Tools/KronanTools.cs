using System.ComponentModel;
using System.Text.Json;
using Kronan.McparIs.KronanApi;
using ModelContextProtocol.Server;

namespace Kronan.McparIs.Tools;

[McpServerToolType]
public sealed class KronanTools(KronanClient kronanClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool, Description("Search for products in the Kronan store by keyword.")]
    public async Task<string> SearchProducts(
        [Description("The search keyword or phrase")] string query,
        [Description("Page number (1-based)")] int page = 1)
    {
        var result = await kronanClient.SearchProductsAsync(query, page);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Get detailed information about a specific product by its SKU.")]
    public async Task<string> GetProduct(
        [Description("The product SKU")] string sku)
    {
        var product = await kronanClient.GetProductAsync(sku);
        if (product is null)
            return $"Product '{sku}' not found.";
        return JsonSerializer.Serialize(product, JsonOptions);
    }

    [McpServerTool, Description("List all product categories available in the Kronan store.")]
    public async Task<string> ListCategories()
    {
        var categories = await kronanClient.GetCategoriesAsync();
        return JsonSerializer.Serialize(categories, JsonOptions);
    }
}
