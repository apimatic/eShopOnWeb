using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values must come from
/// environment variables / user-secrets — never from source-controlled files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from <c>MAXIO_API_KEY</c> / <c>Maxio:ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from <c>MAXIO_SITE_SUBDOMAIN</c> / <c>Maxio:Subdomain</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c> / <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Advanced Billing API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API root from the OpenAPI server template
    /// <c>https://{site}.chargify.com</c>, unless <see cref="BaseUrl"/> is set.
    /// </summary>
    public string ResolveApiBaseUrl()
    {
        EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new BillingConfigurationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY or the Maxio:ApiKey user-secret.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY or the Maxio:ProductFamilyHandle user-secret.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set. Set MAXIO_SITE_SUBDOMAIN or the Maxio:Subdomain user-secret.");
        }
    }
}
