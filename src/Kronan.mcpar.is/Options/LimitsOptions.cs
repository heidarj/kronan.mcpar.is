namespace Kronan.McparIs.Options;

public sealed class LimitsOptions
{
    public const string SectionName = "Limits";
    public int UpstreamRequests { get; set; } = 120;
    public int StartupCooldownSeconds { get; set; } = 200;
    public int PacingMilliseconds { get; set; } = 2000;
    public int ToolRequestsPerMinute { get; set; } = 60;
    public int MutationRequestsPerMinute { get; set; } = 20;
    public int OperationCapacity { get; set; } = 512;
    public int OperationLifetimeSeconds { get; set; } = 1200;
}
