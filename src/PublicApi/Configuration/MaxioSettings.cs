namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via environment variables / user-secrets, never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (from MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN), used to derive the base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base address override. When set, it wins over the subdomain-derived URL.</summary>
    public string? BaseUrl { get; set; }
}
