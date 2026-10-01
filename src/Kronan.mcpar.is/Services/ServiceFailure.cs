namespace Kronan.McparIs.Services;

public sealed class ServiceFailure(string code, string message, int? retryAfterSeconds = null,
    bool outcomeUnknown = false) : Exception(message)
{
    public string Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}
