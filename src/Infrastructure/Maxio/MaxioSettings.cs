using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the "Maxio"
/// configuration section. Values are supplied via user-secrets / environment and must never
/// be committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username (password is a literal "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The handle of the product family whose products are exposed as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> as https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when provided, otherwise
    /// derived from the subdomain. Always returned without a trailing slash.
    /// </summary>
    public string ResolveBaseUrl()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";
        return baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Throws when the settings are not usable, so misconfiguration fails fast at startup with a
    /// clear message. Only references configuration key names, never values.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets (from the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Set it via user-secrets (from MAXIO_SITE_SUBDOMAIN), or provide Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
