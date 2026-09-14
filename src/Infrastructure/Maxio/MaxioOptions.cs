namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values come from user-secrets/environment
/// in every environment - never hard-code a site's credentials or catalog here.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key. Used as the Basic-auth username; password is the literal "x".</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain, e.g. "cp-exp-2" for https://cp-exp-2.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the Product Family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
