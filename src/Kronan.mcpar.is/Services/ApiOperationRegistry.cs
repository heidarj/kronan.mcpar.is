using System.Net;
using System.Text;
using System.Text.Json;

namespace Kronan.McparIs.Services;

public enum ApiOperationMode { Read, Mutation, Commit }
public enum ApiOperationScope { Catalog, ShoppingRead, ShoppingWrite, AccountRead, AccountWrite, PaymentsRead, Commit }

public sealed record ApiOperationDefinition(string Id, HttpMethod Method, string Route, ApiOperationMode Mode,
    ApiOperationScope Scope, bool HasBody = false, bool MayChangeState = false,
    string[]? PathFields = null, string[]? QueryFields = null, int[]? AcceptedNonSuccess = null,
    string[]? RequiredBodyFields = null);

// This registry is the deliberate boundary between MCP arguments and the upstream
// API. A caller can choose an operation's documented arguments, never a URL,
// request header, credential, or another household.
public static class ApiOperationRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<string, ApiOperationDefinition>> operations = new(() =>
        Definitions!.ToDictionary(operation => operation.Id, StringComparer.Ordinal));

    public static ApiOperationDefinition Get(string id) => operations.Value.TryGetValue(id, out var definition)
        ? definition : throw new ServiceFailure("invalid_input", "Unknown Krónan API operation.");

    public static IReadOnlyCollection<ApiOperationDefinition> All => operations.Value.Values.ToArray();

    public static string FeatureDescription(ApiOperationDefinition operation) => operation.Id switch
    {
        var id when id.StartsWith("addresses_", StringComparison.Ordinal) => "delivery addresses",
        var id when id.StartsWith("categories_", StringComparison.Ordinal) => "the selected category",
        var id when id.StartsWith("orders_", StringComparison.Ordinal) => "orders and order history",
        "payments_balance_retrieve" => "the gift-card balance",
        "payments_transactions_retrieve" => "gift-card transactions",
        var id when id.StartsWith("slots_delivery", StringComparison.Ordinal) => "delivery slots",
        var id when id.StartsWith("slots_pickup", StringComparison.Ordinal) => "pickup slots",
        var id when id.StartsWith("checkout_", StringComparison.Ordinal) => "the active checkout",
        var id when id.StartsWith("product_lists_", StringComparison.Ordinal) => "saved product lists",
        var id when id.StartsWith("product_purchase_stats_", StringComparison.Ordinal) => "purchase history",
        var id when id.StartsWith("products_", StringComparison.Ordinal) => "products",
        var id when id.StartsWith("recipes_", StringComparison.Ordinal) => "recipes",
        var id when id.StartsWith("shopping_notes_", StringComparison.Ordinal) => "the shopping note",
        "schema_retrieve" => "the API schema",
        _ => "this feature"
    };

    public static (string Path, JsonElement? Body) Bind(ApiOperationDefinition definition, JsonElement? input)
    {
        if (input is { } raw && raw.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
            throw Invalid("Operation arguments must be an object with optional path, query, and body fields.");
        var request = input is { ValueKind: JsonValueKind.Object } objectRequest ? objectRequest : default;
        ValidateTopLevel(request);
        var pathValues = Property(request, "path");
        var queryValues = Property(request, "query");
        var body = Property(request, "body");

        var path = definition.Route;
        foreach (var field in definition.PathFields ?? [])
        {
            var pathValue = RequiredProperty(pathValues, field);
            path = path.Replace("{" + field + "}", Uri.EscapeDataString(Scalar(pathValue, field)), StringComparison.Ordinal);
        }
        ValidateOnly(pathValues, definition.PathFields ?? [], "path");

        var query = BuildQuery(queryValues, definition.QueryFields ?? []);
        if (query.Length > 0) path += "?" + query;
        if (!definition.HasBody && body is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) })
            throw Invalid("This operation does not accept a request body.");
        if (definition.HasBody && body is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object) })
            throw Invalid("body must be an object matching the documented API request schema.");
        ValidateRequiredBodyFields(body, definition.RequiredBodyFields);
        return (path, definition.HasBody && body is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } ? body.Value.Clone() : null);
    }

    private static void ValidateTopLevel(JsonElement request)
    {
        if (request.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        foreach (var property in request.EnumerateObject())
            if (property.Name is not ("path" or "query" or "body")) throw Invalid("Use only path, query, and body operation arguments.");
    }

    private static JsonElement? Property(JsonElement request, string name)
    {
        if (request.ValueKind == JsonValueKind.Object && request.TryGetProperty(name, out var value))
        {
            if (name is "path" or "query" && value.ValueKind != JsonValueKind.Object) throw Invalid($"{name} must be an object.");
            return value;
        }
        return null;
    }

    private static JsonElement RequiredProperty(JsonElement? element, string field)
    {
        if (element is { ValueKind: JsonValueKind.Object } objectValue && objectValue.TryGetProperty(field, out var value) &&
            value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) return value;
        throw Invalid($"The path.{field} argument is required.");
    }

    private static void ValidateOnly(JsonElement? element, IEnumerable<string> allowed, string location)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectValue) return;
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in objectValue.EnumerateObject())
            if (!names.Contains(property.Name)) throw Invalid($"{location}.{property.Name} is not supported by this operation.");
    }

    private static void ValidateRequiredBodyFields(JsonElement? body, IEnumerable<string>? requiredFields)
    {
        if (requiredFields is null) return;
        if (body is not { ValueKind: JsonValueKind.Object } objectBody)
            throw Invalid($"request.body is required and must contain {string.Join(", ", requiredFields)}.");
        foreach (var field in requiredFields)
            if (!objectBody.TryGetProperty(field, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw Invalid($"request.body.{field} is required.");
    }

    private static string BuildQuery(JsonElement? element, IEnumerable<string> allowed)
    {
        ValidateOnly(element, allowed, "query");
        if (element is not { ValueKind: JsonValueKind.Object } objectValue) return "";
        var parts = new List<string>();
        foreach (var property in objectValue.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray()) parts.Add(Uri.EscapeDataString(property.Name) + "=" + Uri.EscapeDataString(Scalar(item, property.Name)));
            }
            else parts.Add(Uri.EscapeDataString(property.Name) + "=" + Uri.EscapeDataString(Scalar(property.Value, property.Name)));
        }
        return string.Join("&", parts);
    }

    private static string Scalar(JsonElement value, string field) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: > 0 } text ? text : throw Invalid($"{field} cannot be empty."),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => throw Invalid($"{field} must be a string, number, or boolean.")
    };

    private static ServiceFailure Invalid(string message) => new("invalid_input", message);

    private static readonly ApiOperationDefinition[] Definitions =
    [
        new("addresses_list", HttpMethod.Get, "addresses/", ApiOperationMode.Read, ApiOperationScope.AccountRead),
        new("categories_list", HttpMethod.Get, "categories/", ApiOperationMode.Read, ApiOperationScope.Catalog),
        new("categories_products_retrieve", HttpMethod.Get, "categories/{slug}/products/", ApiOperationMode.Read, ApiOperationScope.Catalog, PathFields: ["slug"], QueryFields: ["page"]),
        new("checkout_retrieve", HttpMethod.Get, "checkout/", ApiOperationMode.Read, ApiOperationScope.AccountRead, MayChangeState: true),
        new("checkout_add_to_order_create", HttpMethod.Post, "checkout/add-to-order/", ApiOperationMode.Commit, ApiOperationScope.Commit, MayChangeState: true),
        new("checkout_complete_create", HttpMethod.Post, "checkout/complete/", ApiOperationMode.Commit, ApiOperationScope.Commit, HasBody: true, MayChangeState: true, RequiredBodyFields: ["slotId"]),
        new("checkout_lines_create", HttpMethod.Post, "checkout/lines/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, RequiredBodyFields: ["lines"]),
        new("checkout_preview_lines_create", HttpMethod.Post, "checkout/preview-lines/", ApiOperationMode.Read, ApiOperationScope.AccountRead, HasBody: true, RequiredBodyFields: ["lines"]),
        new("me_retrieve", HttpMethod.Get, "me/", ApiOperationMode.Read, ApiOperationScope.AccountRead),
        new("orders_list", HttpMethod.Get, "orders/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["limit", "offset", "year", "month", "type"]),
        new("orders_retrieve", HttpMethod.Get, "orders/{token}/", ApiOperationMode.Read, ApiOperationScope.AccountRead, PathFields: ["token"]),
        new("orders_delete_lines_create", HttpMethod.Post, "orders/{token}/delete-lines/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["token"], RequiredBodyFields: ["lineIds"]),
        new("orders_lines_toggle_substitution_create", HttpMethod.Post, "orders/{token}/lines-toggle-substitution/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["token"], RequiredBodyFields: ["lineIds"]),
        new("orders_lower_quantity_lines_create", HttpMethod.Post, "orders/{token}/lower-quantity-lines/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["token"], RequiredBodyFields: ["lineIds", "quantity"]),
        new("orders_currently_active_retrieve", HttpMethod.Get, "orders/currently-active/", ApiOperationMode.Read, ApiOperationScope.AccountRead, AcceptedNonSuccess: [404]),
        new("orders_line_summary_retrieve", HttpMethod.Get, "orders/line-summary/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["from_year", "from_month", "to_year", "to_month", "name_contains", "skus"]),
        new("payments_balance_retrieve", HttpMethod.Get, "payments/balance/", ApiOperationMode.Read, ApiOperationScope.PaymentsRead),
        new("payments_transactions_retrieve", HttpMethod.Get, "payments/transactions/", ApiOperationMode.Read, ApiOperationScope.PaymentsRead, QueryFields: ["next_token", "page_size"]),
        new("product_lists_list", HttpMethod.Get, "product-lists/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["limit", "offset"]),
        new("product_lists_create", HttpMethod.Post, "product-lists/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, RequiredBodyFields: ["name"]),
        new("product_lists_retrieve", HttpMethod.Get, "product-lists/{token}/", ApiOperationMode.Read, ApiOperationScope.AccountRead, PathFields: ["token"]),
        new("product_lists_partial_update", HttpMethod.Patch, "product-lists/{token}/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["token"]),
        new("product_lists_destroy", HttpMethod.Delete, "product-lists/{token}/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, MayChangeState: true, PathFields: ["token"]),
        new("product_lists_batch_add_items_create", HttpMethod.Post, "product-lists/{token}/batch-add-items/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["token"], RequiredBodyFields: ["skus"]),
        new("product_lists_delete_all_items_destroy", HttpMethod.Delete, "product-lists/{token}/delete-all-items/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, MayChangeState: true, PathFields: ["token"]),
        new("product_lists_sort_items_create", HttpMethod.Post, "product-lists/{token}/sort-items/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, MayChangeState: true, PathFields: ["token"]),
        new("product_lists_update_item_create", HttpMethod.Post, "product-lists/{token}/update-item/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["token"], RequiredBodyFields: ["sku", "quantity"]),
        new("product_purchase_stats_list", HttpMethod.Get, "product-purchase-stats/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["limit", "offset", "sort", "include_ignored"]),
        new("product_purchase_stats_set_ignored_partial_update", HttpMethod.Patch, "product-purchase-stats/{id}/set-ignored/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, HasBody: true, MayChangeState: true, PathFields: ["id"]),
        new("products_retrieve", HttpMethod.Get, "products/{sku}/", ApiOperationMode.Read, ApiOperationScope.Catalog, PathFields: ["sku"], AcceptedNonSuccess: [404]),
        new("products_barcode_retrieve", HttpMethod.Get, "products/barcode/{barcode}/", ApiOperationMode.Read, ApiOperationScope.Catalog, PathFields: ["barcode"], AcceptedNonSuccess: [404]),
        new("products_batch_create", HttpMethod.Post, "products/batch/", ApiOperationMode.Read, ApiOperationScope.Catalog, HasBody: true, RequiredBodyFields: ["skus"]),
        new("products_by_tag_retrieve", HttpMethod.Get, "products/by-tag/{slug}/", ApiOperationMode.Read, ApiOperationScope.Catalog, PathFields: ["slug"], QueryFields: ["page"]),
        new("products_favorites_retrieve", HttpMethod.Get, "products/favorites/", ApiOperationMode.Read, ApiOperationScope.Catalog, QueryFields: ["page"]),
        new("products_on_sale_retrieve", HttpMethod.Get, "products/on-sale/", ApiOperationMode.Read, ApiOperationScope.Catalog, QueryFields: ["page"]),
        new("products_search_create", HttpMethod.Post, "products/search/", ApiOperationMode.Read, ApiOperationScope.Catalog, HasBody: true, RequiredBodyFields: ["query"]),
        new("products_tags_list", HttpMethod.Get, "products/tags/", ApiOperationMode.Read, ApiOperationScope.Catalog),
        new("recipes_list", HttpMethod.Get, "recipes/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["limit", "offset"]),
        new("recipes_retrieve", HttpMethod.Get, "recipes/{slug}/", ApiOperationMode.Read, ApiOperationScope.AccountRead, PathFields: ["slug"], AcceptedNonSuccess: [404]),
        new("recipes_favorite_create", HttpMethod.Post, "recipes/{slug}/favorite/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, MayChangeState: true, PathFields: ["slug"]),
        new("recipes_favorite_destroy", HttpMethod.Delete, "recipes/{slug}/favorite/", ApiOperationMode.Mutation, ApiOperationScope.AccountWrite, MayChangeState: true, PathFields: ["slug"]),
        new("recipes_favorites_retrieve", HttpMethod.Get, "recipes/favorites/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["limit", "offset"]),
        new("recipes_search_create", HttpMethod.Post, "recipes/search/", ApiOperationMode.Read, ApiOperationScope.AccountRead, HasBody: true),
        new("schema_retrieve", HttpMethod.Get, "schema/", ApiOperationMode.Read, ApiOperationScope.AccountRead, QueryFields: ["format", "lang"]),
        new("shopping_notes_retrieve", HttpMethod.Get, "shopping-notes/", ApiOperationMode.Read, ApiOperationScope.ShoppingRead, MayChangeState: true),
        new("shopping_notes_add_line_create", HttpMethod.Post, "shopping-notes/add-line/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, HasBody: true, MayChangeState: true),
        new("shopping_notes_add_lines_create", HttpMethod.Post, "shopping-notes/add-lines/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, HasBody: true, MayChangeState: true, RequiredBodyFields: ["lines"]),
        new("shopping_notes_change_line_partial_update", HttpMethod.Patch, "shopping-notes/change-line/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, HasBody: true, MayChangeState: true),
        new("shopping_notes_change_placement_partial_update", HttpMethod.Patch, "shopping-notes/change-placement/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, HasBody: true, MayChangeState: true, QueryFields: ["lines_tokens"]),
        new("shopping_notes_delete_line_destroy", HttpMethod.Delete, "shopping-notes/delete-line/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, MayChangeState: true, QueryFields: ["token"]),
        new("shopping_notes_delete_line_archived_destroy", HttpMethod.Delete, "shopping-notes/delete-line-archived/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, MayChangeState: true, QueryFields: ["token"]),
        new("shopping_notes_delete_shopping_note_destroy", HttpMethod.Delete, "shopping-notes/delete-shopping-note/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, MayChangeState: true),
        new("shopping_notes_is_eligible_for_store_product_order_retrieve", HttpMethod.Get, "shopping-notes/is-eligible-for-store-product-order/", ApiOperationMode.Read, ApiOperationScope.ShoppingRead, AcceptedNonSuccess: [404]),
        new("shopping_notes_lines_archived_list", HttpMethod.Get, "shopping-notes/lines-archived/", ApiOperationMode.Read, ApiOperationScope.ShoppingRead),
        new("shopping_notes_product_retrieve", HttpMethod.Get, "shopping-notes/product/", ApiOperationMode.Read, ApiOperationScope.ShoppingRead, QueryFields: ["sku", "barcode"], AcceptedNonSuccess: [404]),
        new("shopping_notes_scan_n_go_stores_list", HttpMethod.Get, "shopping-notes/scan-n-go-stores/", ApiOperationMode.Read, ApiOperationScope.ShoppingRead),
        new("shopping_notes_search_create", HttpMethod.Post, "shopping-notes/search/", ApiOperationMode.Read, ApiOperationScope.ShoppingRead, HasBody: true, RequiredBodyFields: ["query", "store"]),
        new("shopping_notes_store_product_order_create", HttpMethod.Post, "shopping-notes/store-product-order/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, MayChangeState: true),
        new("shopping_notes_toggle_complete_on_line_partial_update", HttpMethod.Patch, "shopping-notes/toggle-complete-on-line/", ApiOperationMode.Mutation, ApiOperationScope.ShoppingWrite, HasBody: true, MayChangeState: true),
        new("slots_delivery_create", HttpMethod.Post, "slots/delivery/", ApiOperationMode.Read, ApiOperationScope.AccountRead, HasBody: true, RequiredBodyFields: ["addressId"]),
        new("slots_delivery_reserve_create", HttpMethod.Post, "slots/delivery/reserve/", ApiOperationMode.Commit, ApiOperationScope.Commit, HasBody: true, MayChangeState: true, RequiredBodyFields: ["addressId", "slotId"]),
        new("slots_pickup_create", HttpMethod.Post, "slots/pickup/", ApiOperationMode.Read, ApiOperationScope.AccountRead, HasBody: true),
        new("slots_pickup_reserve_create", HttpMethod.Post, "slots/pickup/reserve/", ApiOperationMode.Commit, ApiOperationScope.Commit, HasBody: true, MayChangeState: true, RequiredBodyFields: ["slotId"])
    ];
}
