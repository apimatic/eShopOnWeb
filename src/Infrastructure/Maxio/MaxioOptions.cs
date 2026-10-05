using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Expected keys:
///   Maxio:ApiKey                - API key used as the Basic auth username (from MAXIO_API_KEY)
///   Maxio:Subdomain             - site subdomain, e.g. "my-site" (from MAXIO_SITE_SUBDOMAIN)
///   Maxio:ProductFamilyHandle   - handle of the product family holding the plans
///                                 (from MAXIO_DEFAULT_PRODUCT_FAMILY)
///   Maxio:BaseUrl               - optional explicit API base URL; when set it is used
///                                 verbatim instead of deriving one from the subdomain
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When non-empty it is used verbatim as the API base address.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: BaseUrl verbatim when configured,
    /// otherwise https://{Subdomain}.chargify.com.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain.TrimEnd('/')}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured: 'Maxio:ApiKey' is missing (set the MAXIO_API_KEY secret).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured: 'Maxio:ProductFamilyHandle' is missing (set MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain' (MAXIO_SITE_SUBDOMAIN).");
        }
    }
}