using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from environment variables
/// or user-secrets — never from source-controlled settings files.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (Basic-auth username). Maps from <c>MAXIO_API_KEY</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain used to build <c>https://{site}.chargify.com</c>. Maps from <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are listed as plans. Maps from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set, used instead of deriving a URL from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Advanced Billing API base URL. Spec default server is
    /// <c>https://{site}.chargify.com</c>; <see cref="BaseUrl"/> overrides it verbatim.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        // Spec default server: https://{site}.chargify.com (site defaults to "subdomain").
        var site = string.IsNullOrWhiteSpace(Subdomain) ? "subdomain" : Subdomain.Trim();
        return $"https://{site}.chargify.com/";
    }
}
