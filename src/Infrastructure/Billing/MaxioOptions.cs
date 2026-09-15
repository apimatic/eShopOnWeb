using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Bound from Maxio:ApiKey (MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain. Bound from Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are offered as plans. Bound from Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base URL override. Bound from Maxio:BaseUrl.
    /// When set, used verbatim instead of deriving a URL from Subdomain.
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
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
