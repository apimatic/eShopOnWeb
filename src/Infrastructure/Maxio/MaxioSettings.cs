using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration
/// section. Values are supplied via configuration/user-secrets/environment and are never
/// hard-coded, so the same build can run against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key. Used as the HTTP Basic username (password is "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The subdomain of the Advanced Billing site (used to derive the API base URL).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL override. When set, it is used verbatim instead of
    /// deriving one from <see cref="Subdomain"/>. Useful for non-default hosting or a proxy.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: the <see cref="BaseUrl"/> override when present,
    /// otherwise the standard US production template <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request URIs (e.g. "customers.json") compose correctly.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
