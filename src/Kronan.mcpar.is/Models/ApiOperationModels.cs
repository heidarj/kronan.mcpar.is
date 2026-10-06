using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kronan.McparIs.Models;

public sealed record PreparedApiOperation(string OperationId, string Operation, DateTimeOffset ExpiresAt);
public sealed record ApiResult(int StatusCode, JsonElement? Data, bool Replayed = false);

// JsonElement is intentionally not used in public MCP tool signatures: the MCP
// schema generator describes it as a string. JsonObject publishes the structured
// object callers actually send while retaining the API's dynamic field values.
public sealed class ApiOperationRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public JsonObject? Path { get; init; }
    public JsonObject? Query { get; init; }
    public JsonObject? Body { get; init; }

    public JsonElement ToJsonElement() => JsonSerializer.SerializeToElement(this, JsonOptions);
    public static ApiOperationRequest WithQuery(string name, string value) => new()
    { Query = new JsonObject { [name] = value } };
}
