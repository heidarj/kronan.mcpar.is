using System.Text.Json;

namespace Kronan.McparIs.Models;

public sealed record PreparedApiOperation(string OperationId, string Operation, DateTimeOffset ExpiresAt);
public sealed record ApiResult(int StatusCode, JsonElement? Data, bool Replayed = false);
