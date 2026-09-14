using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied
/// via configuration (user-secrets / environment) and are never hard-coded, so the same
/// build runs against any Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Maxio";

    /// <summary>The Maxio Advanced Billing API key (used as the HTTP Basic auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain (e.g. "apimatic-hackathon").</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family whose products are offered as plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL
    /// is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>,
    /// matching the server template in the Maxio OpenAPI specification.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address as an absolute URI, honoring an explicit
    /// <see cref="BaseUrl"/> override and falling back to the subdomain-derived URL.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request paths resolve correctly.
        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Throws if required settings are missing, failing fast at startup.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(ApiKey)} is not configured. Set it via user-secrets or environment.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Either {SectionName}:{nameof(Subdomain)} or {SectionName}:{nameof(BaseUrl)} must be configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(ProductFamilyHandle)} is not configured. Set it via user-secrets or environment.");
        }
    }
}
