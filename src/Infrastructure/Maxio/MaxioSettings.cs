using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the "Maxio" configuration section.
/// Values are supplied at runtime (user-secrets / environment) and are never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain. The API base URL is derived from this unless <see cref="BaseUrl"/> is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim; otherwise the base URL is derived
    /// from <see cref="Subdomain"/> as https://{subdomain}.chargify.com (per the OpenAPI server template).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. Uses <see cref="BaseUrl"/> verbatim when provided, otherwise derives
    /// it from the configured subdomain. The returned URI always ends with a trailing slash so it can be
    /// combined with relative request paths.
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
}
