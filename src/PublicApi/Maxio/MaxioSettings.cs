using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration.
/// Bound from the <c>Maxio</c> configuration section. Values are supplied via
/// .NET user-secrets (never committed to the repository) which are, in turn,
/// populated from the MAXIO_* environment variables on the host.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>Maxio API key (from MAXIO_API_KEY). Used as the HTTP Basic username with password "X".</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN), e.g. "cp-exp-3".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle that contains the subscribable plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL override. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base URL, honoring <see cref="BaseUrl"/> verbatim when provided and
    /// falling back to the subdomain-derived Chargify host otherwise. Always returns a trailing slash so
    /// it can be used directly as an <see cref="System.Net.Http.HttpClient.BaseAddress"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        return new Uri(raw.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
