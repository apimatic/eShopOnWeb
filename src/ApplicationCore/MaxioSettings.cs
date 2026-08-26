namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via environment variables / user-secrets, never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (env var MAXIO_API_KEY), used as the Basic auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (env var MAXIO_SITE_SUBDOMAIN), e.g. "cp-exp-2".</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans (env var MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional verbatim API base URL override; when set it replaces the subdomain-derived URL.</summary>
    public string? BaseUrl { get; set; }
}
