namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (from MAXIO_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override for the Maxio API base address. When set, used verbatim instead of deriving from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}
