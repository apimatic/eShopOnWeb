namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section; values are supplied via user-secrets or
/// environment variables, never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (Maxio:ApiKey, from MAXIO_API_KEY).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Maxio site subdomain (Maxio:Subdomain, from MAXIO_SITE_SUBDOMAIN).
    /// Used to derive the API base address unless <see cref="BaseUrl"/> is set.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the Maxio product family that holds the subscription plans
    /// (Maxio:ProductFamilyHandle, from MAXIO_DEFAULT_PRODUCT_FAMILY).
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address (Maxio:BaseUrl). When set, it is
    /// used verbatim as the base address instead of deriving one from the
    /// subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional Maxio hosting environment ("us" or "eu"). Defaults to "us".
    /// </summary>
    public string? Environment { get; set; }
}
