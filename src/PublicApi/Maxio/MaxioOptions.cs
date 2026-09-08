namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Values are bound from the
/// "Maxio" configuration section (user-secrets / environment variables) and are never
/// hard-coded so the same build can target any Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (HTTP Basic username). Bound from MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Advanced Billing site subdomain. Bound from MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans offered to shoppers.
    /// Bound from MAXIO_DEFAULT_PRODUCT_FAMILY.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Advanced Billing hosting environment used to derive the API base address from
    /// <see cref="Subdomain"/> per the spec's x-server-configuration (US or EU).
    /// </summary>
    public string Environment { get; set; } = "US";
}
