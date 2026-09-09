using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the "Maxio" configuration
/// section. Values are supplied via .NET user-secrets / environment variables and are never
/// stored in the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Site API key (used as the HTTP Basic auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "cp-exp-7". Used to derive the base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim (allowing non-US
    /// hosting or a proxy); otherwise the base URL is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The absolute API base URI. Uses <see cref="BaseUrl"/> verbatim when provided,
    /// otherwise derives the standard US host https://{subdomain}.chargify.com/.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request paths compose correctly.
        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Validates that the settings required to talk to Maxio are present.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Load it into user-secrets from the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured (and no Maxio:BaseUrl override was supplied). " +
                "Load it into user-secrets from the MAXIO_SITE_SUBDOMAIN environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Load it into user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }
}
