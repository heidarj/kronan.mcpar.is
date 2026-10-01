using Kronan.McparIs.Authentication;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Services;

public sealed class ShoppingNoteService(KronanClient client, HouseholdAccess access,
    IOptions<AuthenticationOptions> auth, MutationCoordinator mutations)
{
    public async Task<ShoppingNote> GetAsync(CancellationToken ct)
    {
        access.Require(auth.Value.ShoppingReadScope);
        return await client.GetShoppingNoteAsync(ct) ?? throw new ServiceFailure("upstream_unavailable", "The shopping list is unavailable.");
    }

    public PreparedChange Prepare(ShoppingChange change) =>
        mutations.Prepare(access.Require(auth.Value.ShoppingWriteScope), change);

    public Task<ShoppingNote> ExecuteAsync(string id, ShoppingChange change, CancellationToken ct) =>
        mutations.ExecuteAsync(access.Require(auth.Value.ShoppingWriteScope), id, change, async (normalized, token) =>
        {
            if (normalized.Action != "add")
            {
                var note = await client.GetShoppingNoteAsync(token);
                if (note?.Lines.Any(line => line.Token == normalized.LineToken) != true)
                    throw new ServiceFailure("line_not_found", "That item is not in the connected household's shopping list.");
            }
            return (normalized.Action switch
            {
                "add" => await client.AddLinesAsync(normalized.Items!, token),
                "update" => await client.UpdateLineAsync(normalized.LineToken!.Value, normalized.Text, normalized.Quantity, token),
                "remove" => await client.RemoveLineAsync(normalized.LineToken!.Value, token),
                _ => throw new InvalidOperationException()
            }) ?? throw new ServiceFailure("outcome_unknown", "The change could not be confirmed. Check the list before trying again.", outcomeUnknown: true);
        }, ct);
}
