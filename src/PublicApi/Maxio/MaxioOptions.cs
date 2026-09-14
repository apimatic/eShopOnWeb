namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Values come from environment variables (or user-secrets)
/// and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio site API key (from MAXIO_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim API base address override. When set it is used exactly as
    /// provided instead of deriving "https://{subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// True when the settings needed to talk to Maxio are all present.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
