namespace Kronan.McparIs.Options;

public sealed class KronanOptions
{
    public const string SectionName = "Kronan";

    public string BaseUrl { get; set; } = "https://api.kronan.is/";

    public string? ApiKey { get; set; }
}
