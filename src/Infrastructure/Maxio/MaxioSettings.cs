using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed settings bound from the "Maxio" configuration section. Values are supplied
/// via user-secrets / environment configuration and are never stored in the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio Advanced Billing API key (from MAXIO_API_KEY). Used as HTTP Basic username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain (from MAXIO_SITE_SUBDOMAIN), e.g. "cp-exp-8".</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product family handle whose products are offered as plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute API base URL override. When set it is used verbatim; otherwise the
    /// base URL is derived from <see cref="Subdomain"/> as https://{subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method used when creating subscriptions. Defaults to "remittance"
    /// (invoice billing) so shoppers can subscribe without a stored payment method / 3-DS.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>True when the minimum settings required to reach the API are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the API base address, always terminated with a trailing slash so relative
    /// request paths combine correctly.
    /// </summary>
    public string ResolveBaseUrl()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }
}
