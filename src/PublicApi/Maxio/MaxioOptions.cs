using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are supplied through
/// .NET user-secrets (or environment configuration) and are never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim;
    /// otherwise the base address is derived from the site subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle " +
                "(for example via 'dotnet user-secrets set' or the MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY environment variables).");
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }

    public void EnsureValid()
    {
        ResolveApiBaseUrl();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: Maxio:ApiKey is missing (set it via the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: Maxio:ProductFamilyHandle is missing " +
                "(set it via the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }
    }
}
