using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied via
/// .NET user-secrets / environment configuration and never hard-coded, so the same build can run
/// against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the HTTP Basic auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (e.g. <c>your-site</c>). Used to derive the API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address, honoring the <see cref="BaseUrl"/> override.</summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request URIs resolve predictably.
        if (!raw.EndsWith('/'))
            raw += "/";

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Throws when required settings are missing so misconfiguration fails fast at startup.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Provide it via user-secrets (from MAXIO_API_KEY).");

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) is required unless Maxio:BaseUrl is set.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Provide it via user-secrets (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
    }
}
