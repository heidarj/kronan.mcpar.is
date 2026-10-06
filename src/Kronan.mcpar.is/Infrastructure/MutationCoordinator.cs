using System.Security.Cryptography;
using System.Text.Json;
using Kronan.McparIs.Authentication;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Infrastructure;

// The generation and records are intentionally ephemeral. Old IDs fail closed after restart.
public sealed class MutationCoordinator(TimeProvider time, IOptions<LimitsOptions> options, RequestBudget budget)
{
    private readonly string generation = Guid.NewGuid().ToString("N");
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim mutations = new(1, 1);
    private double Now => time.GetElapsedTime(0, time.GetTimestamp()).TotalSeconds;
    private static string Fingerprint(ShoppingChange change) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(change)));

    public PreparedChange Prepare(HouseholdActor actor, ShoppingChange input)
    {
        var change = ShoppingValidation.Normalize(input);
        lock (gate)
        {
            foreach (var key in entries.Where(p => p.Value.Expires <= Now && !p.Value.Pending).Select(p => p.Key).ToArray()) entries.Remove(key);
            if (entries.Count >= options.Value.OperationCapacity)
                throw new ServiceFailure("operation_capacity", "Too many recent changes are pending. Please wait before preparing another.", 60);
            var id = $"{generation}.{Guid.NewGuid():N}";
            entries.Add(id, new Entry(actor.Subject, Fingerprint(change), Now + options.Value.OperationLifetimeSeconds));
            return new PreparedChange(id, time.GetUtcNow().AddSeconds(options.Value.OperationLifetimeSeconds), change);
        }
    }

    public async Task<ShoppingNote> ExecuteAsync(HouseholdActor actor, string operationId, ShoppingChange input,
        Func<ShoppingChange, CancellationToken, Task<ShoppingNote>> action, CancellationToken ct)
    {
        var normalized = ShoppingValidation.Normalize(input);
        if (!await mutations.WaitAsync(TimeSpan.FromSeconds(1), ct))
            throw new ServiceFailure("concurrent_limit", "Another shopping change is in progress. Retry the same operation ID shortly.", 2);
        try
        {
            Entry entry;
            lock (gate)
            {
                if (operationId is null || operationId.Length != 65 || !operationId.StartsWith(generation + ".", StringComparison.Ordinal) ||
                    !entries.TryGetValue(operationId, out entry!) || entry.Expires <= Now)
                    throw new ServiceFailure("operation_expired", "This prepared change expired or the server restarted. Check the shopping note before preparing a new change.");
                if (entry.Subject != actor.Subject) throw new ServiceFailure("forbidden", "This change belongs to another connected account.");
                if (entry.Fingerprint != Fingerprint(normalized)) throw new ServiceFailure("operation_conflict", "This operation ID was prepared for different arguments.");
                if (entry.Result is { } previous) return previous;
                if (entry.Failure is { } failure) throw failure;
                budget.Mutation();
                entry.Pending = true;
            }
            try
            {
                var note = await action(normalized, ct);
                lock (gate) { entry.Pending = false; entry.Result = note; }
                return note;
            }
            catch (ServiceFailure failure)
            {
                lock (gate)
                {
                    entry.Pending = false;
                    // These failures happen before dispatch, so the same prepared request may be retried.
                    if (failure.Code is not ("rate_limited" or "concurrent_limit" or "upstream_cooldown")) entry.Failure = failure;
                }
                throw;
            }
            catch
            {
                var failure = new ServiceFailure("outcome_unknown", "The change could not be confirmed. Check the shopping note before trying again.", outcomeUnknown: true);
                lock (gate) { entry.Pending = false; entry.Failure = failure; }
                throw failure;
            }
        }
        finally { mutations.Release(); }
    }

    private sealed class Entry(string subject, string fingerprint, double expires)
    {
        public string Subject { get; } = subject;
        public string Fingerprint { get; } = fingerprint;
        public double Expires { get; } = expires;
        public bool Pending { get; set; }
        public ShoppingNote? Result { get; set; }
        public ServiceFailure? Failure { get; set; }
    }
}
