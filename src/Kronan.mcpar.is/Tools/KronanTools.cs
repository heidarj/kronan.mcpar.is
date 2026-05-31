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

    [McpServerTool, Description("Search for products in the Kronan store by keyword. Optionally filter by category.")]
    public async Task<string> SearchProducts(
        [Description("The search keyword or phrase")] string query,
        [Description("Optional category ID to narrow results")] string? categoryId = null,
        [Description("Page number (1-based)")] int page = 1,
        [Description("Number of results per page (max 50)")] int pageSize = 20)
    {
        var result = await kronanClient.SearchProductsAsync(query, page, pageSize, categoryId);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool, Description("Get detailed information about a specific product by its ID.")]
    public async Task<string> GetProduct(
        [Description("The product ID")] string productId)
    {
        var product = await kronanClient.GetProductAsync(productId);
        if (product is null)
            return $"Product '{productId}' not found.";
        return JsonSerializer.Serialize(product, JsonOptions);
    }

    [McpServerTool, Description("List all product categories available in the Kronan store.")]
    public async Task<string> ListCategories()
    {
        var categories = await kronanClient.GetCategoriesAsync();
        return JsonSerializer.Serialize(categories, JsonOptions);
    }

    [McpServerTool, Description("List all Kronan store locations with address and contact information.")]
    public async Task<string> ListStores()
    {
        var stores = await kronanClient.GetStoresAsync();
        return JsonSerializer.Serialize(stores, JsonOptions);
    }

    [McpServerTool, Description("Retrieve current offers and campaign deals from the Kronan store.")]
    public async Task<string> GetOffers()
    {
        var offers = await kronanClient.GetOffersAsync();
        return JsonSerializer.Serialize(offers, JsonOptions);
    }
}
