using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Strongly typed settings bound from the <c>Maxio</c> configuration section.
/// No value is hard-coded: <see cref="ApiKey"/>, <see cref="Subdomain"/> and
/// <see cref="ProductFamilyHandle"/> come from <c>MAXIO_API_KEY</c>,
/// <c>MAXIO_SITE_SUBDOMAIN</c> and <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c> respectively
/// (loaded into user-secrets / configuration at deploy time).
/// <see cref="BaseUrl"/> is an optional override used verbatim when present; otherwise
/// the API address is derived from the site subdomain.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Advanced Billing API key (basic auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, e.g. <c>cp-exp-7</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that backs the subscription catalog.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of
    /// deriving <c>https://{subdomain}.chargify.com</c> from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Whether the section is usable at all (all required values present).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>
    /// The fully qualified API base address (always ends with a single '/').
    /// </summary>
    public Uri BaseUri
    {
        get
        {
            string raw = string.IsNullOrWhiteSpace(BaseUrl)
                ? $"https://{Subdomain}.chargify.com"
                : BaseUrl;
            return new Uri(raw.EndsWith("/", StringComparison.Ordinal) ? raw : raw + "/", UriKind.Absolute);
        }
    }
}
