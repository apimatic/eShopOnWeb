namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key for authentication (Basic Auth username).
    /// Loaded from MAXIO_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain (e.g., "cp-exp-6").
    /// Loaded from MAXIO_SITE_SUBDOMAIN environment variable.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The default product family handle for listing plans.
    /// Loaded from MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional base URL override. When set, used verbatim as the API base address
    /// instead of deriving from subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
