using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. No values are hard-coded: they come from configuration
/// (environment variables / user secrets).
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set it is used verbatim as the API base address instead
    /// of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: the Maxio:ApiKey setting (environment variable MAXIO_API_KEY) is missing or empty.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: provide the Maxio:Subdomain setting (environment variable MAXIO_SITE_SUBDOMAIN) or a Maxio:BaseUrl override.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: the Maxio:ProductFamilyHandle setting (environment variable MAXIO_DEFAULT_PRODUCT_FAMILY) is missing or empty.");
        }
    }

    /// <summary>
    /// Returns the API base address: the <see cref="BaseUrl"/> override verbatim when
    /// present, otherwise the Advanced Billing host derived from the site subdomain. The
    /// host template follows the Maxio OpenAPI server configuration (US by default, EU
    /// when the MAXIO_ENVIRONMENT variable is set to "EU").
    /// </summary>
    public string GetBaseUrl()
    {
        EnsureValid();
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return $"https://{Subdomain}.{host}";
    }
}
