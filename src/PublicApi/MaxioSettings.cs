namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maxio Advanced Billing configuration
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (from MAXIO_API_KEY environment variable)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN environment variable)
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Default product family handle (from MAXIO_DEFAULT_PRODUCT_FAMILY environment variable)
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional base URL override. If not set, derived from subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
