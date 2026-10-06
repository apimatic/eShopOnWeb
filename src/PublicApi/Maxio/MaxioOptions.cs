using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY) or user-secrets; none are hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Billing API base address. When set, it is used verbatim
    /// instead of deriving "https://{subdomain}.chargify.com" from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
