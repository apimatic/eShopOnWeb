using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the "Maxio" configuration section.
/// Values are supplied via environment / user-secrets and must never be hard-coded, so the same build
/// can target a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (used as the HTTP Basic username, with "X" as the password).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-8". Used to derive the API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving one
    /// from <see cref="Subdomain"/>. Useful for pointing at a non-default host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the configured <see cref="BaseUrl"/> when provided, otherwise
    /// the standard Maxio host derived from the subdomain.
    /// </summary>
    public Uri ResolveBaseUrl()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl.Trim();

        // Ensure a trailing slash so relative request paths resolve correctly.
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(baseUrl, UriKind.Absolute);
    }

    /// <summary>Throws if the required settings are missing, with a clear, actionable message.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it in user-secrets or configuration (from MAXIO_API_KEY).");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "Neither Maxio:Subdomain nor Maxio:BaseUrl is configured. Set Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
