using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Options for the Maxio Advanced Billing integration.
///
/// Bound from the "Maxio" configuration section using exactly these keys:
///   Maxio:ApiKey               (from MAXIO_API_KEY)
///   Maxio:Subdomain            (from MAXIO_SITE_SUBDOMAIN)
///   Maxio:ProductFamilyHandle  (from MAXIO_DEFAULT_PRODUCT_FAMILY)
///   Maxio:BaseUrl              (optional override for the API base address)
///
/// No credential value is ever hard-coded here; the values come from configuration
/// (user-secrets / environment) at runtime.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    private const string AdvancedBillingHostSuffix = ".chargify.com";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the absolute base address (with trailing slash) for the Billing API.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var baseUrl = BaseUrl.Trim();
            return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide 'Maxio:ApiKey' and either 'Maxio:Subdomain' or 'Maxio:BaseUrl'.");
        }

        return $"https://{Subdomain.Trim()}{AdvancedBillingHostSuffix}/";
    }

    public string RequireApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide 'Maxio:ApiKey' (from the MAXIO_API_KEY environment variable).");
        }

        return ApiKey;
    }

    public string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide 'Maxio:ProductFamilyHandle' (from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }

        return ProductFamilyHandle;
    }
}
