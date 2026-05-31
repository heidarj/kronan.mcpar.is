using System.Net.Http.Headers;
using System.Net.Http.Json;
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
                new AuthenticationHeaderValue("AccessToken", opts.ApiKey);
        }
    }

    public async Task<ProductSearchResult> SearchProductsAsync(
        string query,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching products: {Query}", query);
        return await SendAsync<ProductSearchResult>(
                   HttpMethod.Post,
                   "products/search/",
                   new
                   {
                       query,
                       page,
                       sortBy = "default",
                       withDetail = true
                   },
                   cancellationToken)
               ?? new ProductSearchResult();
    }

    public async Task<ProductDetail?> GetProductAsync(string sku, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting product: {Sku}", sku);
        return await SendAsync<ProductDetail>(HttpMethod.Get, $"products/{Uri.EscapeDataString(sku)}/", null, cancellationToken);
    }

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching categories");
        return await SendAsync<List<Category>>(HttpMethod.Get, "categories/", null, cancellationToken)
               ?? [];
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
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
