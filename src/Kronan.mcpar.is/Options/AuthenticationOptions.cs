namespace Kronan.McparIs.Options;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";
    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Resource { get; set; } = "";
    public string SubjectClaim { get; set; } = "sub";
    public string[] AllowedSubjects { get; set; } = [];
    public string CatalogScope { get; set; } = "catalog:read";
    public string ShoppingReadScope { get; set; } = "shopping:read";
    public string ShoppingWriteScope { get; set; } = "shopping:write";
    public string AccountReadScope { get; set; } = "account:read";
    public string AccountWriteScope { get; set; } = "account:write";
    public string CheckoutCommitScope { get; set; } = "checkout:commit";
    public string PaymentsReadScope { get; set; } = "payments:read";
    public bool DevelopmentBypass { get; set; }
    public string MetadataUrl => new Uri(new Uri(Resource), "/.well-known/oauth-protected-resource").AbsoluteUri;
    public string[] Scopes => [CatalogScope, ShoppingReadScope, ShoppingWriteScope, AccountReadScope, AccountWriteScope, CheckoutCommitScope, PaymentsReadScope];
}
