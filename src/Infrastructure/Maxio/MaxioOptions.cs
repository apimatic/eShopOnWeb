using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are provided via
/// user-secrets / environment and must never be checked into source control.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The Maxio API key (Basic auth username; the password is "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain (e.g. "cp-exp-3"). Used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family that holds the subscribable plans (e.g. "eshop-subscribe").</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim instead of
    /// deriving "https://{subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the Maxio API base address, honouring the <see cref="BaseUrl"/> override
    /// when present and otherwise deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio:Subdomain is not configured. Provide Maxio:Subdomain (or set the Maxio:BaseUrl override) before using subscription billing.");
        }

        return $"https://{Subdomain.Trim().TrimEnd('.')}.chargify.com";
    }

    /// <summary>Validates the options that are always required, throwing a descriptive exception when missing.</summary>
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured. Set the MAXIO_API_KEY user-secret.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY user-secret.");
        }

        ResolveBaseUrl();
    }
}
