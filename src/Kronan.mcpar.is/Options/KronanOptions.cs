namespace Kronan.McparIs.Options;

public sealed class KronanOptions
{
    public const string SectionName = "Kronan";

    public string BaseUrl { get; set; } = "https://api.kronan.is/api/v1/";

    public string? ApiKey { get; set; }
}
