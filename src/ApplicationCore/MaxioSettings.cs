namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
///
/// Values must come from configuration (environment variables / user secrets) - never hard-coded:
///   Maxio:ApiKey                (MAXIO_API_KEY)                 - Maxio Advanced Billing API key
///   Maxio:Subdomain             (MAXIO_SITE_SUBDOMAIN)          - Maxio site subdomain (e.g. "cp-exp-5")
///   Maxio:ProductFamilyHandle   (MAXIO_DEFAULT_PRODUCT_FAMILY)  - handle of the product family that holds the plans
///   Maxio:BaseUrl               (optional)                      - when set, used verbatim as the API base address
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
