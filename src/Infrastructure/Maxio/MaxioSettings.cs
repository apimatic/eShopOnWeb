using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the "Maxio" configuration section.
/// Values are supplied via .NET user-secrets / environment configuration and are never committed
/// to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>The Maxio API key. Used as the username of the HTTP Basic auth pair (password is "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit base URL override. When set, it is used verbatim as the API base address;
    /// otherwise the base address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: the explicit <see cref="BaseUrl"/> when provided,
    /// otherwise https://{Subdomain}.chargify.com. Always returns a URI ending in a trailing slash
    /// so that relative request paths compose correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Validates that the required settings are present, throwing a descriptive error otherwise.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets from the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Set it via user-secrets from the MAXIO_SITE_SUBDOMAIN environment variable, " +
                "or provide an explicit Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }
}
