using System.Text.Json;
using System.Text.Json.Nodes;
using Kronan.McparIs.Services;
using ModelContextProtocol.Protocol;

namespace Kronan.McparIs.Tools;

public static class ToolResults
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static CallToolResult Success(object data, string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        StructuredContent = JsonSerializer.SerializeToElement(data, JsonOptions)
    };
    public static CallToolResult Failure(ServiceFailure failure) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = failure.Message }],
        StructuredContent = JsonSerializer.SerializeToElement(new { error = new { code = failure.Code, message = failure.Message,
            retryAfterSeconds = failure.RetryAfterSeconds, outcomeUnknown = failure.OutcomeUnknown,
            upstreamStatus = failure.UpstreamStatus } }, JsonOptions)
    };
}
