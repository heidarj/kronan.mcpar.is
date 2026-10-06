using System.ComponentModel;
using Kronan.McparIs.Services;
using Kronan.McparIs.Options;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using JsonElement = Kronan.McparIs.Models.ApiOperationRequest;

namespace Kronan.McparIs.Tools;

// The individual methods intentionally mirror the public OpenAPI operation set.
// `request` is a structured { path?, query?, body? } object. The server validates
// its location and allowed route/query fields before a request can leave the app.
[McpServerToolType, Authorize(Policy = "member")]
public sealed class FullApiTools(ApiOperationService api)
{
    public static bool TryGetRequiredScope(string toolName, AuthenticationOptions auth, out string scope)
    {
        if (toolName is "GetCategoryProducts" or "GetProductByBarcode" or "GetProductsBatch" or "ListProductsByTag" or
            "ListFavoriteProducts" or "ListProductsOnSale" or "ListProductTags") { scope = auth.CatalogScope; return true; }
        if (toolName is "AddShoppingNoteItem" or "ReorderShoppingNoteLines" or "DeleteArchivedShoppingNoteLine" or
            "ClearShoppingNote" or "SortShoppingNoteByStore" or "ToggleShoppingNoteLineCompletion") { scope = auth.ShoppingWriteScope; return true; }
        if (toolName is "CheckShoppingNoteStoreOrderEligibility" or "ListArchivedShoppingNoteLines" or "GetStoreProduct" or
            "ListScanAndGoStores" or "SearchStoreProducts") { scope = auth.ShoppingReadScope; return true; }
        if (toolName is "GetGiftCardBalance" or "ListGiftCardTransactions") { scope = auth.PaymentsReadScope; return true; }
        if (toolName is "AddActiveCheckoutToOrder" or "CompleteCheckout" or "ReserveDeliverySlot" or "ReservePickupSlot") { scope = auth.CheckoutCommitScope; return true; }
        if (toolName is "CreateProductList" or "UpdateProductList" or "DeleteProductList" or "BatchAddProductListItems" or
            "ClearProductList" or "SortProductListItems" or "UpdateProductListItem" or "SetProductPurchaseIgnored" or
            "FavoriteRecipe" or "UnfavoriteRecipe" or "UpdateCheckoutLines" or "DeleteOrderLines" or
            "ToggleOrderLineSubstitution" or "LowerOrderLineQuantities") { scope = auth.AccountWriteScope; return true; }
        if (toolName is "PrepareApiOperation" or "ListAddresses" or "GetActiveCheckout" or "PreviewCheckoutLines" or
            "GetIdentity" or "ListOrders" or "GetOrder" or "GetActiveOrder" or "GetOrderLineSummary" or
            "ListProductLists" or "GetProductList" or "ListProductPurchaseStats" or "ListRecipes" or "GetRecipe" or
            "ListFavoriteRecipes" or "SearchRecipes" or "GetApiSchema" or "ListDeliverySlots" or "ListPickupSlots")
        { scope = auth.AccountReadScope; return true; }
        scope = ""; return false;
    }

    [McpServerTool(Name = "PrepareApiOperation", ReadOnly = false, Destructive = false, Idempotent = false),
     Description("Prepare a non-read Krónan API operation. Send the exact operation name and the same structured request that the matching mutation tool will receive. Reuse the returned operation ID only for that exact action.")]
    public CallToolResult PrepareApiOperation(string operation, JsonElement? request = null) =>
        ToolResults.Success(new { prepared = api.Prepare(operation, request?.ToJsonElement()) }, "Operation prepared.");

    [McpServerTool(Name = "ListAddresses", ReadOnly = true, Idempotent = true), Description("List delivery addresses belonging to the connected identity.")]
    public Task<CallToolResult> ListAddresses(CancellationToken cancellationToken = default) => Read("addresses_list", null, cancellationToken);

    [McpServerTool(Name = "GetCategoryProducts", ReadOnly = true, Idempotent = true), Description("List products for a category. request.path.slug is required; request.query.page is optional.")]
    public Task<CallToolResult> GetCategoryProducts(JsonElement request, CancellationToken cancellationToken = default) => Read("categories_products_retrieve", request, cancellationToken);

    [McpServerTool(Name = "GetActiveCheckout", ReadOnly = false, Idempotent = false), Description("Get the connected identity's active checkout. Krónan may create an empty checkout if none exists.")]
    public Task<CallToolResult> GetActiveCheckout(CancellationToken cancellationToken = default) => Read("checkout_retrieve", null, cancellationToken);

    [McpServerTool(Name = "AddActiveCheckoutToOrder", ReadOnly = false, Destructive = false, Idempotent = false), Description("Add the active checkout to the active order using a prepared operation ID. This committing operation is disabled by default.")]
    public Task<CallToolResult> AddActiveCheckoutToOrder(string operationId, CancellationToken cancellationToken = default) => Change("checkout_add_to_order_create", operationId, null, cancellationToken);

    [McpServerTool(Name = "CompleteCheckout", ReadOnly = false, Destructive = false, Idempotent = false), Description("Complete an active checkout using request.body with the documented slotId, optional addressId, and returnBags. Disabled by default because it can create an order.")]
    public Task<CallToolResult> CompleteCheckout(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("checkout_complete_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "UpdateCheckoutLines", ReadOnly = false, Destructive = true, Idempotent = false), Description("Update checkout lines using request.body. The upstream replace field defaults to true, so set it explicitly after reviewing existing checkout lines.")]
    public Task<CallToolResult> UpdateCheckoutLines(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("checkout_lines_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "PreviewCheckoutLines", ReadOnly = true, Idempotent = true), Description("Validate proposed checkout product lines and return availability and estimated subtotal without changing the checkout. request.body.lines is required.")]
    public Task<CallToolResult> PreviewCheckoutLines(JsonElement request, CancellationToken cancellationToken = default) => Read("checkout_preview_lines_create", request, cancellationToken);

    [McpServerTool(Name = "GetIdentity", ReadOnly = true, Idempotent = true), Description("Get the type and display name of the identity represented by the configured Krónan token.")]
    public Task<CallToolResult> GetIdentity(CancellationToken cancellationToken = default) => Read("me_retrieve", null, cancellationToken);

    [McpServerTool(Name = "ListOrders", ReadOnly = true, Idempotent = true), Description("List orders. request.query supports limit, offset, year, month, and type.")]
    public Task<CallToolResult> ListOrders(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("orders_list", request, cancellationToken);

    [McpServerTool(Name = "GetOrder", ReadOnly = true, Idempotent = true), Description("Get one order. request.path.token is required.")]
    public Task<CallToolResult> GetOrder(JsonElement request, CancellationToken cancellationToken = default) => Read("orders_retrieve", request, cancellationToken);

    [McpServerTool(Name = "DeleteOrderLines", ReadOnly = false, Destructive = true, Idempotent = false), Description("Delete eligible lines from an existing order. request.path.token and request.body line IDs are required.")]
    public Task<CallToolResult> DeleteOrderLines(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("orders_delete_lines_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "ToggleOrderLineSubstitution", ReadOnly = false, Destructive = false, Idempotent = false), Description("Toggle substitution permission for existing order lines. This is a non-idempotent upstream toggle.")]
    public Task<CallToolResult> ToggleOrderLineSubstitution(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("orders_lines_toggle_substitution_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "LowerOrderLineQuantities", ReadOnly = false, Destructive = true, Idempotent = false), Description("Lower quantities for eligible order lines. A quantity of zero removes a line.")]
    public Task<CallToolResult> LowerOrderLineQuantities(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("orders_lower_quantity_lines_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "GetActiveOrder", ReadOnly = true, Idempotent = true), Description("Get the active-order summary, or a distinct 404 result when no active order exists.")]
    public Task<CallToolResult> GetActiveOrder(CancellationToken cancellationToken = default) => Read("orders_currently_active_retrieve", null, cancellationToken);

    [McpServerTool(Name = "GetOrderLineSummary", ReadOnly = true, Idempotent = true), Description("Summarize fulfilled historical order lines. request.query supports date range, name_contains, or up to ten skus.")]
    public Task<CallToolResult> GetOrderLineSummary(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("orders_line_summary_retrieve", request, cancellationToken);

    [McpServerTool(Name = "GetGiftCardBalance", ReadOnly = true, Idempotent = true), Description("Get the balance of the connected identity's bound gift card.")]
    public Task<CallToolResult> GetGiftCardBalance(CancellationToken cancellationToken = default) => Read("payments_balance_retrieve", null, cancellationToken);

    [McpServerTool(Name = "ListGiftCardTransactions", ReadOnly = true, Idempotent = true), Description("List bound gift-card transactions. request.query supports next_token and page_size.")]
    public Task<CallToolResult> ListGiftCardTransactions(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("payments_transactions_retrieve", request, cancellationToken);

    [McpServerTool(Name = "ListProductLists", ReadOnly = true, Idempotent = true), Description("List named saved product lists. request.query supports limit and offset.")]
    public Task<CallToolResult> ListProductLists(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("product_lists_list", request, cancellationToken);

    [McpServerTool(Name = "CreateProductList", ReadOnly = false, Destructive = false, Idempotent = false), Description("Create a named saved product list using request.body.")]
    public Task<CallToolResult> CreateProductList(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "GetProductList", ReadOnly = true, Idempotent = true), Description("Get one saved product list and its products. request.path.token is required.")]
    public Task<CallToolResult> GetProductList(JsonElement request, CancellationToken cancellationToken = default) => Read("product_lists_retrieve", request, cancellationToken);

    [McpServerTool(Name = "UpdateProductList", ReadOnly = false, Destructive = false, Idempotent = false), Description("Update a saved product list's name or description.")]
    public Task<CallToolResult> UpdateProductList(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_partial_update", operationId, request, cancellationToken);

    [McpServerTool(Name = "DeleteProductList", ReadOnly = false, Destructive = true, Idempotent = false), Description("Permanently delete one saved product list and all its items.")]
    public Task<CallToolResult> DeleteProductList(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_destroy", operationId, request, cancellationToken);

    [McpServerTool(Name = "BatchAddProductListItems", ReadOnly = false, Destructive = false, Idempotent = false), Description("Add up to 30 SKUs to a saved product list. Products already on the list are skipped upstream.")]
    public Task<CallToolResult> BatchAddProductListItems(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_batch_add_items_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "ClearProductList", ReadOnly = false, Destructive = true, Idempotent = false), Description("Remove every item from a saved product list without deleting the list itself.")]
    public Task<CallToolResult> ClearProductList(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_delete_all_items_destroy", operationId, request, cancellationToken);

    [McpServerTool(Name = "SortProductListItems", ReadOnly = false, Destructive = false, Idempotent = false), Description("Sort a saved product list's items by store department.")]
    public Task<CallToolResult> SortProductListItems(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_sort_items_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "UpdateProductListItem", ReadOnly = false, Destructive = true, Idempotent = false), Description("Add or change a product-list item quantity. An upstream quantity of zero removes the item.")]
    public Task<CallToolResult> UpdateProductListItem(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_lists_update_item_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "ListProductPurchaseStats", ReadOnly = true, Idempotent = true), Description("List personal purchase statistics. request.query supports pagination, ordering, and include_ignored.")]
    public Task<CallToolResult> ListProductPurchaseStats(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("product_purchase_stats_list", request, cancellationToken);

    [McpServerTool(Name = "SetProductPurchaseIgnored", ReadOnly = false, Destructive = false, Idempotent = false), Description("Set whether a purchase-history product is ignored.")]
    public Task<CallToolResult> SetProductPurchaseIgnored(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("product_purchase_stats_set_ignored_partial_update", operationId, request, cancellationToken);

    [McpServerTool(Name = "GetProductByBarcode", ReadOnly = true, Idempotent = true), Description("Look up a product by barcode. request.path.barcode is required.")]
    public Task<CallToolResult> GetProductByBarcode(JsonElement request, CancellationToken cancellationToken = default) => Read("products_barcode_retrieve", request, cancellationToken);

    [McpServerTool(Name = "GetProductsBatch", ReadOnly = true, Idempotent = true), Description("Look up up to 100 product SKUs. request.body.skus is required.")]
    public Task<CallToolResult> GetProductsBatch(JsonElement request, CancellationToken cancellationToken = default) => Read("products_batch_create", request, cancellationToken);

    [McpServerTool(Name = "ListProductsByTag", ReadOnly = true, Idempotent = true), Description("List products with a tag. request.path.slug is required; request.query.page is optional.")]
    public Task<CallToolResult> ListProductsByTag(JsonElement request, CancellationToken cancellationToken = default) => Read("products_by_tag_retrieve", request, cancellationToken);

    [McpServerTool(Name = "ListFavoriteProducts", ReadOnly = true, Idempotent = true), Description("List frequently purchased favorite products.")]
    public Task<CallToolResult> ListFavoriteProducts(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("products_favorites_retrieve", request, cancellationToken);

    [McpServerTool(Name = "ListProductsOnSale", ReadOnly = true, Idempotent = true), Description("List current home-delivery products on sale.")]
    public Task<CallToolResult> ListProductsOnSale(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("products_on_sale_retrieve", request, cancellationToken);

    [McpServerTool(Name = "ListProductTags", ReadOnly = true, Idempotent = true), Description("List product tag slugs for use with ListProductsByTag.")]
    public Task<CallToolResult> ListProductTags(CancellationToken cancellationToken = default) => Read("products_tags_list", null, cancellationToken);

    [McpServerTool(Name = "ListRecipes", ReadOnly = true, Idempotent = true), Description("List published recipes. request.query supports limit and offset.")]
    public Task<CallToolResult> ListRecipes(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("recipes_list", request, cancellationToken);

    [McpServerTool(Name = "GetRecipe", ReadOnly = true, Idempotent = true), Description("Get a recipe with ingredients and product availability. request.path.slug is required.")]
    public Task<CallToolResult> GetRecipe(JsonElement request, CancellationToken cancellationToken = default) => Read("recipes_retrieve", request, cancellationToken);

    [McpServerTool(Name = "FavoriteRecipe", ReadOnly = false, Destructive = false, Idempotent = false), Description("Add a recipe to favorites.")]
    public Task<CallToolResult> FavoriteRecipe(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("recipes_favorite_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "UnfavoriteRecipe", ReadOnly = false, Destructive = true, Idempotent = false), Description("Remove a recipe from favorites.")]
    public Task<CallToolResult> UnfavoriteRecipe(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("recipes_favorite_destroy", operationId, request, cancellationToken);

    [McpServerTool(Name = "ListFavoriteRecipes", ReadOnly = true, Idempotent = true), Description("List favorite recipes.")]
    public Task<CallToolResult> ListFavoriteRecipes(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("recipes_favorites_retrieve", request, cancellationToken);

    [McpServerTool(Name = "SearchRecipes", ReadOnly = true, Idempotent = true), Description("Search recipes using documented request.body filters.")]
    public Task<CallToolResult> SearchRecipes(JsonElement request, CancellationToken cancellationToken = default) => Read("recipes_search_create", request, cancellationToken);

    [McpServerTool(Name = "GetApiSchema", ReadOnly = true, Idempotent = true), Description("Get Krónan's OpenAPI schema document. This may be large; inspect only the relevant operation section.")]
    public Task<CallToolResult> GetApiSchema(JsonElement? request = null, CancellationToken cancellationToken = default) =>
        Read("schema_retrieve", request ?? JsonElement.WithQuery("format", "json"), cancellationToken);

    [McpServerTool(Name = "AddShoppingNoteItem", ReadOnly = false, Destructive = false, Idempotent = false), Description("Add one free-text or SKU item to the shopping note using documented request.body.")]
    public Task<CallToolResult> AddShoppingNoteItem(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("shopping_notes_add_line_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "ReorderShoppingNoteLines", ReadOnly = false, Destructive = false, Idempotent = false), Description("Reorder shopping-note lines. request.query.lines_tokens is a sequence of documented line tokens.")]
    public Task<CallToolResult> ReorderShoppingNoteLines(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("shopping_notes_change_placement_partial_update", operationId, request, cancellationToken);

    [McpServerTool(Name = "DeleteArchivedShoppingNoteLine", ReadOnly = false, Destructive = true, Idempotent = false), Description("Delete one archived shopping-note line using request.query.token.")]
    public Task<CallToolResult> DeleteArchivedShoppingNoteLine(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("shopping_notes_delete_line_archived_destroy", operationId, request, cancellationToken);

    [McpServerTool(Name = "ClearShoppingNote", ReadOnly = false, Destructive = true, Idempotent = false), Description("Delete all lines from the shopping note while preserving the note itself.")]
    public Task<CallToolResult> ClearShoppingNote(string operationId, CancellationToken cancellationToken = default) => Change("shopping_notes_delete_shopping_note_destroy", operationId, null, cancellationToken);

    [McpServerTool(Name = "CheckShoppingNoteStoreOrderEligibility", ReadOnly = true, Idempotent = true), Description("Check whether the shopping note contains products eligible for store-product ordering. A 204 means eligible and 404 means no matching products.")]
    public Task<CallToolResult> CheckShoppingNoteStoreOrderEligibility(CancellationToken cancellationToken = default) => Read("shopping_notes_is_eligible_for_store_product_order_retrieve", null, cancellationToken);

    [McpServerTool(Name = "ListArchivedShoppingNoteLines", ReadOnly = true, Idempotent = true), Description("List completed and archived shopping-note lines.")]
    public Task<CallToolResult> ListArchivedShoppingNoteLines(CancellationToken cancellationToken = default) => Read("shopping_notes_lines_archived_list", null, cancellationToken);

    [McpServerTool(Name = "GetStoreProduct", ReadOnly = true, Idempotent = true), Description("Look up a Scan and Go product by request.query.sku or request.query.barcode.")]
    public Task<CallToolResult> GetStoreProduct(JsonElement request, CancellationToken cancellationToken = default) => Read("shopping_notes_product_retrieve", request, cancellationToken);

    [McpServerTool(Name = "ListScanAndGoStores", ReadOnly = true, Idempotent = true), Description("List Scan and Go stores and their ext_id values.")]
    public Task<CallToolResult> ListScanAndGoStores(CancellationToken cancellationToken = default) => Read("shopping_notes_scan_n_go_stores_list", null, cancellationToken);

    [McpServerTool(Name = "SearchStoreProducts", ReadOnly = true, Idempotent = true), Description("Search a selected Scan and Go store's product selection. request.body requires query and store; store is the ext_id returned by ListScanAndGoStores.")]
    public Task<CallToolResult> SearchStoreProducts(JsonElement request, CancellationToken cancellationToken = default) => Read("shopping_notes_search_create", request, cancellationToken);

    [McpServerTool(Name = "SortShoppingNoteByStore", ReadOnly = false, Destructive = false, Idempotent = false), Description("Request Krónan's documented store-product ordering for the shopping note. This endpoint has no request body.")]
    public Task<CallToolResult> SortShoppingNoteByStore(string operationId, CancellationToken cancellationToken = default) => Change("shopping_notes_store_product_order_create", operationId, null, cancellationToken);

    [McpServerTool(Name = "ToggleShoppingNoteLineCompletion", ReadOnly = false, Destructive = false, Idempotent = false), Description("Toggle a shopping-note line's completion state. This is a non-idempotent upstream toggle.")]
    public Task<CallToolResult> ToggleShoppingNoteLineCompletion(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("shopping_notes_toggle_complete_on_line_partial_update", operationId, request, cancellationToken);

    [McpServerTool(Name = "ListDeliverySlots", ReadOnly = true, Idempotent = true), Description("List delivery slots. request.body.addressId is required and must come from ListAddresses.")]
    public Task<CallToolResult> ListDeliverySlots(JsonElement request, CancellationToken cancellationToken = default) => Read("slots_delivery_create", request, cancellationToken);

    [McpServerTool(Name = "ReserveDeliverySlot", ReadOnly = false, Destructive = false, Idempotent = false), Description("Reserve a delivery slot using documented request.body. Disabled by default because it can authorize an amount and create an order context.")]
    public Task<CallToolResult> ReserveDeliverySlot(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("slots_delivery_reserve_create", operationId, request, cancellationToken);

    [McpServerTool(Name = "ListPickupSlots", ReadOnly = true, Idempotent = true), Description("List pickup stores and slots. request.body may select a documented chain.")]
    public Task<CallToolResult> ListPickupSlots(JsonElement? request = null, CancellationToken cancellationToken = default) => Read("slots_pickup_create", request, cancellationToken);

    [McpServerTool(Name = "ReservePickupSlot", ReadOnly = false, Destructive = false, Idempotent = false), Description("Reserve a pickup slot using documented request.body. Disabled by default because it can authorize an amount and create an order context.")]
    public Task<CallToolResult> ReservePickupSlot(string operationId, JsonElement request, CancellationToken cancellationToken = default) => Change("slots_pickup_reserve_create", operationId, request, cancellationToken);

    private async Task<CallToolResult> Read(string operation, JsonElement? request, CancellationToken cancellationToken) =>
        ToolResults.Success(new { result = await api.ReadAsync(operation, request?.ToJsonElement(), cancellationToken) }, "Krónan API operation completed.");

    private async Task<CallToolResult> Change(string operation, string operationId, JsonElement? request, CancellationToken cancellationToken) =>
        ToolResults.Success(new { result = await api.ExecuteAsync(operation, operationId, request?.ToJsonElement(), cancellationToken) }, "Krónan API operation completed. Refresh the resource to inspect current state.");
}
