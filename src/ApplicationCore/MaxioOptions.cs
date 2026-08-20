using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Maxio Advanced Billing settings bound from the <c>Maxio</c> configuration section.
/// Values come from environment variables / user-secrets — never from committed files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the HTTP Basic username. Bound from <c>MAXIO_API_KEY</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain used to derive the API host. Bound from <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose products are exposed as plans. Bound from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base address. When set, used verbatim instead of deriving a host from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Subdomain)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>
    /// Resolves the Billing API base address. A configured <see cref="BaseUrl"/> wins;
    /// otherwise the host is <c>https://{Subdomain}.chargify.com/</c> (US) per Maxio's documented site URL.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseUrl(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return NormalizeBaseUrl($"https://{Subdomain.Trim()}.chargify.com");
    }

    internal static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }
}
