using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are supplied via
/// environment variables / .NET user-secrets and must never be committed to
/// the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the HTTP Basic auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain (e.g. the value before ".chargify.com").</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim as the API
    /// base address instead of being derived from <see cref="Subdomain"/>. Useful
    /// for pointing the same build at a different Maxio site or a proxy.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address as an absolute URI ending with a
    /// trailing slash. Prefers <see cref="BaseUrl"/> when provided; otherwise
    /// derives the standard Chargify/Maxio host from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseUrl()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request paths compose correctly.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>
    /// True when the minimum settings required to talk to Maxio are present
    /// (API key, a base URL or subdomain, and a product family handle).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain))
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>Validates that the settings required to talk to Maxio are present.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set the MAXIO_API_KEY secret (e.g. via user-secrets).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN secret, or provide Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY secret.");
        }
    }
}
