using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings used to talk to the Maxio Advanced Billing API.
/// Bound from the "Maxio" configuration section.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. From the MAXIO_API_KEY environment variable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain. From the MAXIO_SITE_SUBDOMAIN environment variable.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the Maxio product family that holds the subscription plans. From the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional full API base address. When set it is used verbatim; otherwise the base address is
    /// derived from <see cref="Subdomain"/> as https://{subdomain}.chargify.com.
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
            throw new MaxioConfigurationException(
                $"{SectionName}:BaseUrl is not set and {SectionName}:Subdomain is not configured, so the Maxio API base address cannot be determined.");
        }

        return $"https://{Subdomain!.Trim()}.chargify.com";
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException($"{SectionName}:ApiKey is not configured.");
        }

        ResolveBaseUrl();

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException($"{SectionName}:ProductFamilyHandle is not configured.");
        }
    }
}
