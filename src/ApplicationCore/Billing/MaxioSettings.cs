using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are supplied via
/// .NET user-secrets / environment variables and must never be committed.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username (from MAXIO_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. "acme" in acme.chargify.com (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product family handle whose products are exposed as subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim as the API base address
    /// instead of being derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when the minimum required settings to talk to Maxio are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when provided,
    /// otherwise https://{subdomain}.chargify.com.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.TrimEnd('/') + "/";
            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Provide either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
