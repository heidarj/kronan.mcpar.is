namespace Kronan.McparIs.Models;

public sealed record ShoppingNote(Guid Token, string Name, IReadOnlyList<ShoppingLine> Lines);
public sealed record ShoppingLine(Guid Token, string? Text, int? Quantity, ShoppingProduct? Product,
    int Placement, bool IsCompleted);
public sealed record ShoppingProduct(string? Sku, string Name, string? Description, string? Thumbnail);
public sealed record ShoppingItemInput(string? Text = null, string? Sku = null, int Quantity = 1);
public sealed record ShoppingChange(string Action, ShoppingItemInput[]? Items = null, Guid? LineToken = null,
    string? Text = null, int? Quantity = null);
public sealed record PreparedChange(string OperationId, DateTimeOffset ExpiresAt, ShoppingChange Change);
