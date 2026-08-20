using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from environment
/// variables / user-secrets — never from source.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio:ApiKey — sourced from MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio:Subdomain — sourced from MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio:ProductFamilyHandle — sourced from MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Maxio:BaseUrl — optional. When set, used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/> and the spec server template.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Advanced Billing API root from the spec's US production server
    /// <c>https://{site}.chargify.com</c>, unless <see cref="BaseUrl"/> overrides it.
    /// </summary>
    public string ResolveApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseUrl(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio is not configured: set Maxio:BaseUrl or Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN).");
        }

        return NormalizeBaseUrl($"https://{Subdomain.Trim()}.chargify.com");
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                "Maxio is not configured: set Maxio:ApiKey (MAXIO_API_KEY).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio is not configured: set Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        _ = ResolveApiBaseUrl();
    }

    internal static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed + "/";
    }
}
