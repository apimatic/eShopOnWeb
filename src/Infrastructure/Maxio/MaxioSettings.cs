using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Values are supplied via .NET user-secrets / environment configuration and
/// are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigSection = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain (e.g. "your-site"). Used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim (a trailing slash is ensured so
    /// relative resource paths resolve correctly) instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when enough is configured to reach the Maxio API.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the API base address: the verbatim <see cref="BaseUrl"/> when provided, otherwise
    /// https://{Subdomain}.chargify.com. A trailing slash is appended so that relative request paths
    /// (e.g. "product_families.json") combine correctly against it.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        string raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
