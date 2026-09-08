namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section (values arrive via .NET user-secrets / environment and
/// are never committed to the repository).
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (Basic auth username). Source: MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. cp-exp-5. Source: MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans. Source: MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Maxio API base address. When set it is used verbatim instead
    /// of deriving https://{subdomain}.chargify.com.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Whole-call budget for a single Maxio operation, in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>True when the settings needed to talk to Maxio are all present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Subdomain)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}
