using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets or environment variables — never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Billing API key (sourced from the MAXIO_API_KEY environment variable).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (sourced from the MAXIO_SITE_SUBDOMAIN environment variable).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscription plans (sourced from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!
            : $"https://{Subdomain}.chargify.com";
        return new Uri(baseUrl.TrimEnd('/') + "/");
    }
}
