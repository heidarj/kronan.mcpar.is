using System.Text.Json;
using Kronan.McparIs.Services;

namespace Kronan.McparIs.Tests;

public sealed class ApiOperationRegistryTests
{
    [Fact]
    public void Published_operation_registry_has_all_63_distinct_openapi_operations()
    {
        var operations = ApiOperationRegistry.All;
        Assert.Equal(63, operations.Count);
        Assert.Equal(63, operations.Select(operation => operation.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(operations, operation => operation.Id == "checkout_retrieve" && operation.MayChangeState);
        Assert.Contains(operations, operation => operation.Id == "slots_delivery_reserve_create" && operation.Mode == ApiOperationMode.Commit);
    }

    [Fact]
    public void Registry_only_builds_documented_routes_and_query_fields()
    {
        using var document = JsonDocument.Parse("{\"path\":{\"token\":\"a/b\"}}");
        var bound = ApiOperationRegistry.Bind(ApiOperationRegistry.Get("product_lists_retrieve"), document.RootElement);
        Assert.Equal("product-lists/a%2Fb/", bound.Path);

        using var queryDocument = JsonDocument.Parse("{\"query\":{\"skus\":[\"one\",\"two\"]}}");
        var summary = ApiOperationRegistry.Bind(ApiOperationRegistry.Get("orders_line_summary_retrieve"), queryDocument.RootElement);
        Assert.Equal("orders/line-summary/?skus=one&skus=two", summary.Path);
    }

    [Fact]
    public void Registry_rejects_undeclared_query_and_body_locations()
    {
        using var extraQuery = JsonDocument.Parse("{\"query\":{\"not_a_parameter\":true}}");
        var queryError = Assert.Throws<ServiceFailure>(() => ApiOperationRegistry.Bind(ApiOperationRegistry.Get("orders_list"), extraQuery.RootElement));
        Assert.Equal("invalid_input", queryError.Code);

        using var body = JsonDocument.Parse("{\"body\":{\"anything\":true}}");
        var bodyError = Assert.Throws<ServiceFailure>(() => ApiOperationRegistry.Bind(ApiOperationRegistry.Get("orders_list"), body.RootElement));
        Assert.Equal("invalid_input", bodyError.Code);

        using var scalarBody = JsonDocument.Parse("{\"body\":[\"not-an-object\"]}");
        var scalarError = Assert.Throws<ServiceFailure>(() => ApiOperationRegistry.Bind(ApiOperationRegistry.Get("products_batch_create"), scalarBody.RootElement));
        Assert.Equal("invalid_input", scalarError.Code);

        using var deliveryWithoutAddress = JsonDocument.Parse("{\"body\":{}}");
        var requiredBodyError = Assert.Throws<ServiceFailure>(() => ApiOperationRegistry.Bind(ApiOperationRegistry.Get("slots_delivery_create"), deliveryWithoutAddress.RootElement));
        Assert.Equal("invalid_input", requiredBodyError.Code);
        Assert.Contains("addressId", requiredBodyError.Message);
    }
}
