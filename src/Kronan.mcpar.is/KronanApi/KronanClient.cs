using System.Net.Http.Headers;
using System.Text.Json;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.KronanApi;

public sealed class KronanClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KronanClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public KronanClient(HttpClient httpClient, IOptions<KronanOptions> options, ILogger<KronanClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var opts = options.Value;
        _httpClient.BaseAddress = new Uri(opts.BaseUrl);

        if (!string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        }
    }

    public async Task<ProductSearchResult> SearchProductsAsync(
        string query,
        int page = 1,
        int pageSize = 20,
        string? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/products?query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(categoryId))
            url += $"&categoryId={Uri.EscapeDataString(categoryId)}";

        _logger.LogInformation("Searching products: {Query}", query);
        return await GetAsync<ProductSearchResult>(url, cancellationToken)
               ?? new ProductSearchResult();
    }

    public async Task<ProductDetail?> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting product: {ProductId}", productId);
        return await GetAsync<ProductDetail>($"api/products/{Uri.EscapeDataString(productId)}", cancellationToken);
    }

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching categories");
        return await GetAsync<List<Category>>("api/categories", cancellationToken)
               ?? [];
    }

    public async Task<List<Store>> GetStoresAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching stores");
        return await GetAsync<List<Store>>("api/stores", cancellationToken)
               ?? [];
    }

    public async Task<List<Offer>> GetOffersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching offers");
        return await GetAsync<List<Offer>>("api/offers", cancellationToken)
               ?? [];
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed for {Url}", url);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization failed for {Url}", url);
            throw;
        }
    }
}
