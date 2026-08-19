using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Configuration for Maxio Advanced Billing. Bound from the <c>Maxio</c> section.
/// Secret values must come from environment variables or user-secrets — never from source control.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio site API key. Bound from <c>Maxio:ApiKey</c> / <c>MAXIO_API_KEY</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain. Bound from <c>Maxio:Subdomain</c> / <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Product family API handle whose products are offered as subscription plans.
    /// Bound from <c>Maxio:ProductFamilyHandle</c> / <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base URL override. When set, used verbatim instead of
    /// <c>https://{Subdomain}.chargify.com</c>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY or the Maxio:ApiKey user secret.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY or the Maxio:ProductFamilyHandle user secret.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Configure Maxio:BaseUrl or Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN).");
        }
    }
}
