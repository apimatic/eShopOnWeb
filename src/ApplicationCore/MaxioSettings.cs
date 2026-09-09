namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Configuration settings for the Maxio (Advanced Billing) integration.
/// Bound from the "Maxio" configuration section; values are supplied via
/// user secrets or environment variables and must never be hard-coded here.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// Maxio API key (from MAXIO_API_KEY).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Maxio environment, e.g. US (from MAXIO_ENVIRONMENT).
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans
    /// (from MAXIO_DEFAULT_PRODUCT_FAMILY).
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional handle of the plan to subscribe to when the caller does not
    /// specify one.
    /// </summary>
    public string DefaultPlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override for the Maxio API base address. When set it
    /// is used instead of deriving a host from the subdomain/environment.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
