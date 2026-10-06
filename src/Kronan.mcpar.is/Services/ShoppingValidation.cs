using Kronan.McparIs.Models;

namespace Kronan.McparIs.Services;

public static class ShoppingValidation
{
    public static ShoppingChange Normalize(ShoppingChange change)
    {
        var action = change.Action?.Trim().ToLowerInvariant();
        switch (action)
        {
            case "add":
                if (change.Items is not { Length: >= 1 and <= 30 } || change.LineToken is not null || change.Text is not null || change.Quantity is not null)
                    throw Invalid("Add between one and thirty items; do not supply line-update fields.");
                var items = change.Items.Select(item =>
                {
                    if (item is null) throw Invalid("Every item must contain text or a SKU.");
                    var text = item.Text?.Trim();
                    var sku = item.Sku?.Trim();
                    if ((text is not null) == (sku is not null)) throw Invalid("Each item needs exactly one of text or SKU.");
                    if (text is not null) Text(text);
                    if (sku is not null) Sku(sku);
                    Quantity(item.Quantity);
                    return new ShoppingItemInput(text, sku, item.Quantity);
                }).ToArray();
                return new ShoppingChange("add", items);
            case "update":
                Line(change.LineToken);
                if (change.Items is not null || (change.Text is null && change.Quantity is null))
                    throw Invalid("An update needs text or quantity. Empty updates are refused because Krónan treats them as deletion.");
                var updatedText = change.Text?.Trim();
                if (updatedText is not null) Text(updatedText);
                if (change.Quantity is { } quantity) Quantity(quantity);
                return new ShoppingChange("update", LineToken: change.LineToken, Text: updatedText, Quantity: change.Quantity);
            case "remove":
                Line(change.LineToken);
                if (change.Items is not null || change.Text is not null || change.Quantity is not null)
                    throw Invalid("A removal takes only a line identifier.");
                return new ShoppingChange("remove", LineToken: change.LineToken);
            default: throw Invalid("Choose add, update, or remove.");
        }
    }

    public static void Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl))
            throw Invalid("Item text must contain between one and 255 characters without control characters.");
    }
    public static void Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32 || value.Any(char.IsControl))
            throw Invalid("A SKU must contain between one and 32 characters.");
    }
    private static void Quantity(int value) { if (value is < 0 or > 10000) throw Invalid("Quantity must be between zero and 10000."); }
    private static void Line(Guid? value) { if (value is null || value == Guid.Empty) throw Invalid("A valid line UUID is required."); }
    private static ServiceFailure Invalid(string message) => new("invalid_input", message);
}
