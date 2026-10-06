using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.KronanApi;

public sealed class KronanClient
{
    private readonly HttpClient http;
    private readonly RequestBudget budget;
    private readonly TimeProvider time;
    private readonly bool configured;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public KronanClient(HttpClient http, IOptions<KronanOptions> options, RequestBudget budget, TimeProvider time)
    {
        this.http = http;
        this.budget = budget;
        this.time = time;
        http.BaseAddress = new Uri(options.Value.BaseUrl);
        configured = !string.IsNullOrWhiteSpace(options.Value.ApiKey);
        if (configured) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AccessToken", options.Value.ApiKey);
    }

    public Task<ProductSearchResult?> SearchProductsAsync(string query, int page, int pageSize, string? sortBy,
        bool withDetail, bool includePurchaseHistory, CancellationToken ct) =>
        SendAsync<ProductSearchResult>(HttpMethod.Post, "products/search/",
            new { query, page, pageSize, sortBy, withDetail, includePurchaseHistory }, false, ct);
    public Task<ProductDetail?> GetProductAsync(string sku, CancellationToken ct) =>
        SendAsync<ProductDetail>(HttpMethod.Get, $"products/{Uri.EscapeDataString(sku)}/", null, false, ct, allowNotFound: true);
    public Task<List<Category>?> GetCategoriesAsync(CancellationToken ct) =>
        SendAsync<List<Category>>(HttpMethod.Get, "categories/", null, false, ct);
    // The documented GET auto-creates a note. It is deliberately never retried or cached.
    public Task<ShoppingNote?> GetShoppingNoteAsync(CancellationToken ct) =>
        SendAsync<ShoppingNote>(HttpMethod.Get, "shopping-notes/", null, true, ct);
    public Task<ShoppingNote?> AddLinesAsync(ShoppingItemInput[] items, CancellationToken ct)
    {
        var change = ShoppingValidation.Normalize(new("add", items));
        return SendAsync<ShoppingNote>(HttpMethod.Post, "shopping-notes/add-lines/", new { lines = change.Items }, true, ct);
    }
    public Task<ShoppingNote?> UpdateLineAsync(Guid token, string? text, int? quantity, CancellationToken ct)
    {
        var change = ShoppingValidation.Normalize(new("update", LineToken: token, Text: text, Quantity: quantity));
        return SendAsync<ShoppingNote>(HttpMethod.Patch, "shopping-notes/change-line/", new { token, text = change.Text, quantity = change.Quantity }, true, ct);
    }
    public Task<ShoppingNote?> RemoveLineAsync(Guid token, CancellationToken ct) =>
        SendAsync<ShoppingNote>(HttpMethod.Delete, $"shopping-notes/delete-line/?token={token:D}", null, true, ct);

    // Used only by the registered OpenAPI operation wrappers. This deliberately
    // takes a relative path; callers cannot supply a host, credentials, or headers.
    public async Task<ApiResponse> SendOperationAsync(HttpMethod method, string relativePath, JsonElement? body,
        bool mayChangeState, IReadOnlySet<HttpStatusCode>? acceptedNonSuccess, string permissionFeature, CancellationToken ct)
    {
        if (!configured) throw new ServiceFailure("connection_required", "The server's Krónan connection has not been configured.");
        if (string.IsNullOrWhiteSpace(relativePath) || Uri.TryCreate(relativePath, UriKind.Absolute, out _) ||
            relativePath.Contains("..", StringComparison.Ordinal))
            throw new ServiceFailure("invalid_input", "The requested API operation is invalid.");

        using var permit = await budget.EnterUpstreamAsync(ct);
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is { } payload) request.Content = JsonContent.Create(payload, options: JsonOptions);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (acceptedNonSuccess?.Contains(response.StatusCode) == true)
                return new ApiResponse((int)response.StatusCode, null);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is { } date ? date - time.GetUtcNow() : TimeSpan.FromSeconds(200));
                delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 86400));
                budget.Cooldown(delay);
                throw new ServiceFailure("upstream_rate_limited", "Krónan asked us to wait before sending more requests.", (int)Math.Ceiling(delay.TotalSeconds));
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ServiceFailure("connection_rejected", "Krónan rejected the server credential. Its owner needs to reconnect or rotate it.", upstreamStatus: (int)response.StatusCode);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new ServiceFailure("permission_denied", $"This Krónan access token does not have permission to access {permissionFeature}. Update this token’s permissions in Krónan’s access-key settings: https://kronan.is/adgangur/adgangslyklar", upstreamStatus: (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new ServiceFailure(mayChangeState && (int)response.StatusCode >= 500 ? "outcome_unknown" : "upstream_rejected",
                    mayChangeState && (int)response.StatusCode >= 500 ? "The change could not be confirmed. Check the current state before trying again." :
                    $"Krónan rejected this request (HTTP {(int)response.StatusCode}). Check the documented arguments for {permissionFeature}.",
                    outcomeUnknown: mayChangeState && (int)response.StatusCode >= 500, upstreamStatus: (int)response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
                return new ApiResponse((int)response.StatusCode, null);
            var content = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            return new ApiResponse((int)response.StatusCode, content);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            throw new ServiceFailure(mayChangeState ? "outcome_unknown" : "upstream_unavailable",
                mayChangeState ? "The change could not be confirmed. Check the current state before trying again." : "Krónan is temporarily unavailable.",
                outcomeUnknown: mayChangeState);
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, bool mayChangeState,
        CancellationToken ct, bool allowNotFound = false)
    {
        if (!configured) throw new ServiceFailure("connection_required", "The server's Krónan connection has not been configured.");
        using var permit = await budget.EnterUpstreamAsync(ct);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is { } date ? date - time.GetUtcNow() : TimeSpan.FromSeconds(200));
                delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 86400));
                budget.Cooldown(delay);
                throw new ServiceFailure("upstream_rate_limited", "Krónan asked us to wait before sending more requests.", (int)Math.Ceiling(delay.TotalSeconds));
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ServiceFailure("connection_rejected", "Krónan rejected the server credential. Its owner needs to reconnect or rotate it.");
            if (!response.IsSuccessStatusCode)
                throw new ServiceFailure(mayChangeState && (int)response.StatusCode >= 500 ? "outcome_unknown" : "upstream_rejected",
                    mayChangeState && (int)response.StatusCode >= 500 ? "The change could not be confirmed. Check the list before trying again." : "Krónan could not complete this request.",
                    outcomeUnknown: mayChangeState && (int)response.StatusCode >= 500);
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            if (result is null) throw new JsonException("Missing response.");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            throw new ServiceFailure(mayChangeState ? "outcome_unknown" : "upstream_unavailable",
                mayChangeState ? "The change could not be confirmed. Check the list before trying again." : "Krónan is temporarily unavailable.",
                outcomeUnknown: mayChangeState);
        }
    }
}

public sealed record ApiResponse(int StatusCode, JsonElement? Content);
