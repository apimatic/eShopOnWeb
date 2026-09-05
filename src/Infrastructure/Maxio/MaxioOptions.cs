namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values come from environment/user-secrets -
/// never hard-code a real site's credentials or catalog here.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSection = "Maxio";

    /// <summary>Maxio Advanced Billing API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "acme" for acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of the
    /// subdomain-derived "https://{Subdomain}.chargify.com" address (e.g. for EU-hosted sites).
    /// </summary>
    public string? BaseUrl { get; set; }
}
