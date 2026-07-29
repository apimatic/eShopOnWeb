using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the <c>Maxio:</c> configuration
/// section. Values are supplied via .NET user-secrets / environment configuration
/// and are never hard-coded, so the same build can target a different Maxio site
/// and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (e.g. <c>acme</c> for <c>https://acme.chargify.com</c>).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the
    /// base URL is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> when provided, otherwise
    /// <c>https://{Subdomain}.chargify.com/</c>. Always returned with a trailing slash
    /// so relative request URIs resolve correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Throws when required settings are missing, failing fast at startup.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"{SectionName}:ApiKey is not configured. Set it via user-secrets from the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException($"{SectionName}:Subdomain is not configured. Set it via user-secrets from the MAXIO_SITE_SUBDOMAIN environment variable (or provide {SectionName}:BaseUrl).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException($"{SectionName}:ProductFamilyHandle is not configured. Set it via user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }
}
