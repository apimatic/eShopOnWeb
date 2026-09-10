using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied
/// via configuration (user-secrets / environment) and never hard-coded, so the same build
/// can target a different Maxio site and catalog.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username (password is literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL
    /// is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>
    /// per the Maxio server template.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base URL, honoring the <see cref="BaseUrl"/> override.</summary>
    public Uri ResolveBaseUrl()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl!.Trim();

        // Ensure a trailing slash so relative request paths resolve predictably.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Validates that the minimum settings required to talk to Maxio are present.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it in user-secrets (from the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Set it in user-secrets (from MAXIO_SITE_SUBDOMAIN), or set Maxio:BaseUrl explicitly.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it in user-secrets (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
