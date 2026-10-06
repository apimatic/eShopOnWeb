using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio:" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (from MAXIO_API_KEY).
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Maxio Advanced Billing site subdomain (from MAXIO_SITE_SUBDOMAIN).
    /// </summary>
    public string Subdomain { get; set; } = "";

    /// <summary>
    /// Handle of the product family that holds the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).
    /// </summary>
    public string ProductFamilyHandle { get; set; } = "";

    /// <summary>
    /// Maxio hosting environment: "US" (default) or "EU" (from MAXIO_ENVIRONMENT).
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address to use for Maxio Advanced Billing requests.
    /// </summary>
    public string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        string host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return $"https://{Subdomain}.{host}";
    }
}
