using System.Security.Cryptography;
using System.Text;
using Kronan.McparIs.Authentication;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Services;

public sealed class CatalogService(KronanClient client, HouseholdAccess access, CatalogCache cache,
    IOptions<AuthenticationOptions> auth, IOptions<KronanOptions> kronan)
{
    private string Partition => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kronan.Value.BaseUrl + "\n" + kronan.Value.ApiKey)));
    public async Task<ProductSearchResult> SearchAsync(string query, int page, int pageSize, string? sortBy,
        bool withDetail, bool includePurchaseHistory, CancellationToken ct)
    {
        access.Require(auth.Value.CatalogScope);
        query = query?.Trim() ?? "";
        sortBy = string.IsNullOrWhiteSpace(sortBy) ? "default" : sortBy.Trim();
        if (query.Length is < 1 or > 64 || query.Any(char.IsControl) || page is < 1 or > 1000 ||
            pageSize is < 1 or > 100 || sortBy.Length > 64 || sortBy.Any(char.IsControl))
            throw new ServiceFailure("invalid_input", "Use a query of one to 64 characters, a page between one and 1000, a page size between one and 100, and a short sort value.");
        var result = await cache.GetAsync($"{Partition}:search:{page}:{pageSize}:{sortBy}:{withDetail}:{includePurchaseHistory}:{query}", TimeSpan.FromMinutes(2),
            token => client.SearchProductsAsync(query, page, pageSize, sortBy, withDetail, includePurchaseHistory, token), ct) ?? new ProductSearchResult();
        result.Hits = result.Hits.Take(pageSize).ToList();
        return result;
    }

    public Task<ProductDetail?> ProductAsync(string sku, CancellationToken ct)
    {
        access.Require(auth.Value.CatalogScope);
        sku = sku?.Trim() ?? "";
        ShoppingValidation.Sku(sku);
        return cache.GetAsync($"{Partition}:product:{sku}", TimeSpan.FromMinutes(2), token => client.GetProductAsync(sku, token), ct);
    }

    public async Task<List<Category>> CategoriesAsync(CancellationToken ct)
    {
        access.Require(auth.Value.CatalogScope);
        return await cache.GetAsync($"{Partition}:categories", TimeSpan.FromMinutes(15), client.GetCategoriesAsync, ct) ?? [];
    }
}
