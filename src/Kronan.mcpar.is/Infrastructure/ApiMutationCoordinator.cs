using System.Security.Cryptography;
using System.Text.Json;
using Kronan.McparIs.Authentication;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Infrastructure;

// The endpoint operation ledger is deliberately bounded and process-local. It
// prevents accidental repeat dispatches during one server lifetime; it is not a
// durable transaction log and old operation IDs fail closed after a restart.
public sealed class ApiMutationCoordinator(TimeProvider time, IOptions<LimitsOptions> limits, RequestBudget budget)
{
    private readonly string generation = Guid.NewGuid().ToString("N");
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim mutations = new(1, 1);
    private double Now => time.GetElapsedTime(0, time.GetTimestamp()).TotalSeconds;

    public PreparedApiOperation Prepare(HouseholdActor actor, ApiOperationDefinition operation, JsonElement? request)
    {
        var normalized = Normalize(request);
        lock (gate)
        {
            foreach (var key in entries.Where(pair => pair.Value.Expires <= Now && !pair.Value.Pending).Select(pair => pair.Key).ToArray()) entries.Remove(key);
            if (entries.Count >= limits.Value.OperationCapacity)
                throw new ServiceFailure("operation_capacity", "Too many recent operations are pending. Please wait before preparing another.", 60);
            var id = $"{generation}.{Guid.NewGuid():N}";
            entries.Add(id, new Entry(actor.Subject, operation.Id, Fingerprint(normalized), Now + limits.Value.OperationLifetimeSeconds));
            return new PreparedApiOperation(id, operation.Id, time.GetUtcNow().AddSeconds(limits.Value.OperationLifetimeSeconds));
        }
    }

    public async Task<ApiResult> ExecuteAsync(HouseholdActor actor, ApiOperationDefinition operation, string operationId,
        JsonElement? request, Func<CancellationToken, Task<ApiResult>> action, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        if (!await mutations.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken))
            throw new ServiceFailure("concurrent_limit", "Another account change is in progress. Retry the same operation ID shortly.", 2);
        try
        {
            Entry entry;
            lock (gate)
            {
                if (operationId is null || operationId.Length != 65 || !operationId.StartsWith(generation + ".", StringComparison.Ordinal) ||
                    !entries.TryGetValue(operationId, out entry!) || entry.Expires <= Now)
                    throw new ServiceFailure("operation_expired", "This prepared operation expired or the server restarted. Refresh the resource before preparing a new operation.");
                if (entry.Subject != actor.Subject) throw new ServiceFailure("forbidden", "This operation belongs to another connected account.");
                if (entry.Operation != operation.Id || entry.Fingerprint != Fingerprint(normalized))
                    throw new ServiceFailure("operation_conflict", "This operation ID was prepared for different arguments.");
                if (entry.Result is { } previous) return previous with { Replayed = true };
                if (entry.Failure is { } failure) throw failure;
                budget.Mutation();
                entry.Pending = true;
            }
            try
            {
                var result = await action(cancellationToken);
                lock (gate) { entry.Pending = false; entry.Result = result; }
                return result;
            }
            catch (ServiceFailure failure)
            {
                lock (gate)
                {
                    entry.Pending = false;
                    if (failure.Code is not ("rate_limited" or "concurrent_limit" or "upstream_cooldown")) entry.Failure = failure;
                }
                throw;
            }
            catch
            {
                var failure = new ServiceFailure("outcome_unknown", "The change could not be confirmed. Refresh the resource before trying again.", outcomeUnknown: true);
                lock (gate) { entry.Pending = false; entry.Failure = failure; }
                throw failure;
            }
        }
        finally { mutations.Release(); }
    }

    private static JsonElement Normalize(JsonElement? request) => request?.Clone() ?? JsonDocument.Parse("{}").RootElement.Clone();
    private static string Fingerprint(JsonElement request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));

    private sealed class Entry(string subject, string operation, string fingerprint, double expires)
    {
        public string Subject { get; } = subject;
        public string Operation { get; } = operation;
        public string Fingerprint { get; } = fingerprint;
        public double Expires { get; } = expires;
        public bool Pending { get; set; }
        public ApiResult? Result { get; set; }
        public ServiceFailure? Failure { get; set; }
    }
}
