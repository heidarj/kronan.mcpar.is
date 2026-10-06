using System.Net;
using System.Text.Json;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Tests;

public sealed class KronanClientTests
{
    [Fact]
    public async Task Search_sends_documented_parameters_and_decodes_nested_discount()
    {
        using var handler = new StubHandler(async (request, ct) =>
        {
            Assert.Equal("AccessToken", request.Headers.Authorization?.Scheme);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("products/search/", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.True(body.RootElement.GetProperty("withDetail").GetBoolean());
            Assert.Equal(20, body.RootElement.GetProperty("pageSize").GetInt32());
            return StubHandler.Json("{\"count\":1,\"page\":1,\"hits\":[{\"sku\":\"123\",\"name\":\"Butter\",\"price\":500,\"detail\":{\"discountedPrice\":400,\"discountPercent\":20,\"onSale\":true,\"tags\":[]}}]}");
        });
        var result = await TestSupport.Client(handler).SearchProductsAsync("Butter", 1, 20, "default", true, false, default);
        Assert.Equal(400, Assert.Single(result!.Hits).Detail!.DiscountedPrice);
    }

    [Fact]
    public async Task Product_404_is_a_normal_not_found_result()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json("{}", HttpStatusCode.NotFound)));
        Assert.Null(await TestSupport.Client(handler).GetProductAsync("missing", default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Batch_add_uses_one_call_and_omits_null_sku_fields()
    {
        using var handler = new StubHandler(async (request, ct) =>
        {
            Assert.EndsWith("shopping-notes/add-lines/", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var lines = body.RootElement.GetProperty("lines");
            Assert.Equal(3, lines.GetArrayLength());
            Assert.False(lines[0].TryGetProperty("sku", out _));
            Assert.Equal(1, lines[0].GetProperty("quantity").GetInt32());
            return StubHandler.Json(TestSupport.NoteJson, HttpStatusCode.Created);
        });
        var note = await TestSupport.Client(handler).AddLinesAsync([new("Butter"), new("Milk"), new("Eggs")], default);
        Assert.Single(note!.Lines); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Upstream_429_sets_shared_cooldown_and_does_not_retry()
    {
        var clock = new TestClock(); var budget = new RequestBudget(clock, Microsoft.Extensions.Options.Options.Create(TestSupport.FastLimits()));
        using var handler = new StubHandler((_, _) =>
        {
            var response = StubHandler.Json("private upstream details", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(17)); return Task.FromResult(response);
        });
        var client = TestSupport.Client(handler, clock, budget);
        var first = await Assert.ThrowsAsync<ServiceFailure>(() => client.GetCategoriesAsync(default));
        Assert.Equal(17, first.RetryAfterSeconds); Assert.DoesNotContain("private", first.Message);
        await Assert.ThrowsAsync<ServiceFailure>(() => client.GetShoppingNoteAsync(default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Forbidden_operation_reports_missing_feature_permission_without_claiming_the_token_is_invalid()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json("{}", HttpStatusCode.Forbidden)));
        var error = await Assert.ThrowsAsync<ServiceFailure>(() => TestSupport.Client(handler).SendOperationAsync(
            HttpMethod.Get, "addresses/", null, false, null, "delivery addresses", default));
        Assert.Equal("permission_denied", error.Code);
        Assert.Equal(403, error.UpstreamStatus);
        Assert.Contains("delivery addresses", error.Message);
        Assert.Contains("kronan.is/adgangur/adgangslyklar", error.Message);
        Assert.DoesNotContain("rotate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unauthorized_operation_still_reports_a_rejected_credential()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json("{}", HttpStatusCode.Unauthorized)));
        var error = await Assert.ThrowsAsync<ServiceFailure>(() => TestSupport.Client(handler).SendOperationAsync(
            HttpMethod.Get, "addresses/", null, false, null, "delivery addresses", default));
        Assert.Equal("connection_rejected", error.Code);
        Assert.Equal(401, error.UpstreamStatus);
    }

    [Fact]
    public async Task Rejected_operation_includes_only_safe_status_and_feature_context()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json("private diagnostic", HttpStatusCode.BadRequest)));
        var error = await Assert.ThrowsAsync<ServiceFailure>(() => TestSupport.Client(handler).SendOperationAsync(
            HttpMethod.Get, "categories/bad/products/", null, false, null, "the selected category", default));
        Assert.Equal("upstream_rejected", error.Code);
        Assert.Equal(400, error.UpstreamStatus);
        Assert.Contains("HTTP 400", error.Message);
        Assert.Contains("selected category", error.Message);
        Assert.DoesNotContain("private diagnostic", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_write_or_auto_creating_get_reports_uncertainty_without_retry(bool write)
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException("secret upstream response"));
        var client = TestSupport.Client(handler);
        var error = await Assert.ThrowsAsync<ServiceFailure>(() => write ? client.AddLinesAsync([new("Butter")], default) : client.GetShoppingNoteAsync(default));
        Assert.True(error.OutcomeUnknown); Assert.DoesNotContain("secret", error.Message); Assert.Equal(1, handler.Calls);
    }
}
