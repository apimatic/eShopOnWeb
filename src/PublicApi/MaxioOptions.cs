namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Configuration for Maxio Advanced Billing integration.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key for Basic Authentication (username=key, password=x).
    /// Bound from MAXIO_API_KEY env var or Maxio:ApiKey config.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (e.g. "cp-exp-6" for cp-exp-6.chargify.com).
    /// Bound from MAXIO_SITE_SUBDOMAIN env var or Maxio:Subdomain config.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Product family handle to list plans from.
    /// Bound from MAXIO_DEFAULT_PRODUCT_FAMILY env var or Maxio:ProductFamilyHandle config.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base URL.
    /// When set, used verbatim instead of deriving from subdomain.
    /// Bound from Maxio:BaseUrl config.
    /// </summary>
    public string? BaseUrl { get; set; }
}
