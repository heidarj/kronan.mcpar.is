using System.ComponentModel;
using Kronan.McparIs.Models;
using Kronan.McparIs.Services;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kronan.McparIs.Tools;

[McpServerToolType]
public sealed class ShoppingNoteTools(ShoppingNoteService shopping)
{
    [Authorize(Policy = "shopping.read")]
    [McpServerTool(Name = "GetShoppingList", ReadOnly = false, Destructive = false, Idempotent = false),
     Description("Get the connected household's current shopping note. Krónan automatically creates a note if none exists. Use this to inspect uncertain changes before preparing another.")]
    public async Task<CallToolResult> GetShoppingList(CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { note = await shopping.GetAsync(cancellationToken) }, "Current shopping list.");

    [Authorize(Policy = "shopping.write")]
    [McpServerTool(Name = "PrepareShoppingChange", ReadOnly = false, Destructive = false, Idempotent = false),
     Description("Prepare one user-requested add, update, or remove action. Returns an operation ID bound to these arguments. Reuse that ID when retrying the matching mutation; preparing a new ID means a new action. For generic groceries use text; choose a SKU only when the user requests a specific product. Batch up to 30 additions.")]
    public CallToolResult PrepareShoppingChange(ShoppingChange change) =>
        ToolResults.Success(new { prepared = shopping.Prepare(change) }, "Change prepared; invoke the matching mutation with this operation ID and the same arguments.");

    [Authorize(Policy = "shopping.write")]
    [McpServerTool(Name = "AddShoppingItems", ReadOnly = false, Destructive = false, Idempotent = false),
     Description("Add items using an ID from PrepareShoppingChange(action=add). Send exactly the prepared items. Quantity defaults to one. On uncertain outcome, inspect the list; never silently prepare another addition.")]
    public async Task<CallToolResult> AddShoppingItems(string operationId, ShoppingItemInput[] items, CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { note = await shopping.ExecuteAsync(operationId, new ShoppingChange("add", items), cancellationToken) }, "Items added. A replay with the same ID returns the previous result.");

    [Authorize(Policy = "shopping.write")]
    [McpServerTool(Name = "UpdateShoppingItem", ReadOnly = false, Destructive = true, Idempotent = false),
     Description("Update a household item using an ID prepared for action=update. Supply the same line token and text and/or quantity. An empty update is refused.")]
    public async Task<CallToolResult> UpdateShoppingItem(string operationId, Guid lineToken, string? text = null,
        int? quantity = null, CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { note = await shopping.ExecuteAsync(operationId, new ShoppingChange("update", LineToken: lineToken, Text: text, Quantity: quantity), cancellationToken) }, "Item updated.");

    [Authorize(Policy = "shopping.write")]
    [McpServerTool(Name = "RemoveShoppingItem", ReadOnly = false, Destructive = true, Idempotent = false),
     Description("Remove a household item using an ID prepared for action=remove and the same line token. Use only when the user asks to remove it.")]
    public async Task<CallToolResult> RemoveShoppingItem(string operationId, Guid lineToken, CancellationToken cancellationToken = default) =>
        ToolResults.Success(new { note = await shopping.ExecuteAsync(operationId, new ShoppingChange("remove", LineToken: lineToken), cancellationToken) }, "Item removed.");
}
