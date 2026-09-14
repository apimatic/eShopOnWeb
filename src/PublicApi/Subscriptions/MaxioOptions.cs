using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Settings used to talk to the Maxio Advanced Billing API.
/// Bound from the <c>Maxio</c> configuration section. No secrets should ever be
/// hard-coded in this repository; the API key is loaded from .NET user-secrets.
/// </summary>
public class MaxioOptions
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Basic-auth API key issued for the site. Comes from the MAXIO_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain (e.g. "cp-exp-8"). Comes from the MAXIO_SITE_SUBDOMAIN environment variable.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans (e.g. "eshop-subscribe").
    /// Comes from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Data-center environment ("US", "EU", ...). Comes from the MAXIO_ENVIRONMENT environment variable.
    /// Only used to derive the API base URL when <see cref="BaseUrl"/> is not supplied.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional override for the Advanced Billing API base address. When set it is used verbatim,
    /// otherwise the base URL is derived from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is required when Maxio:BaseUrl is not configured.");
        }

        string hostSuffix = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return $"https://{Subdomain}.{hostSuffix}";
    }
}
