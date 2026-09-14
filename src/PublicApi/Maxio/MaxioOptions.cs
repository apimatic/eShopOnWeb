using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings used to reach the Maxio Advanced Billing REST API.
/// Bound from the "Maxio" configuration section. Values are supplied at
/// runtime via user-secrets and/or environment variables; no value is ever
/// committed to source control.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic auth username). Source: MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain used to derive the API host. Source: MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family API handle that contains the subscription plans. Source: MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute base URL. When set it is used verbatim instead of the
    /// value derived from the subdomain. Source: MAXIO_BASE_URL.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. Prefers <see cref="BaseUrl"/> when
    /// provided; otherwise derives https://{subdomain}.chargify.com.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/'), UriKind.Absolute);
        }

        return new Uri($"https://{Subdomain}.chargify.com", UriKind.Absolute);
    }
}
