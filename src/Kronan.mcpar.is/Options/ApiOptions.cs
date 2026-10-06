namespace Kronan.McparIs.Options;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    // Placing an order, completing checkout, or reserving a slot can have financial
    // consequences. They remain registered tools, but production dispatch is off
    // until the household deliberately enables it after its own review process.
    public bool EnableCommitOperations { get; set; }
}
