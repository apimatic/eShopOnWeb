namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Bound from the <c>Maxio:</c> configuration section. No value is hard-coded.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Basic-auth username (the API key). Secret: user-secrets / environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site; fills the <c>{site}</c> of the Production server URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are the offered plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set, used verbatim as the API base address instead of the subdomain-derived one.</summary>
    public string? BaseUrl { get; set; }
}
