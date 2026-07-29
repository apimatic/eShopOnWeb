using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the "Maxio" configuration section.
/// Values must come from configuration (user-secrets / environment variables) and are never hard-coded,
/// so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio API key. Used as the HTTP Basic auth username (with the literal password "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is derived
    /// from <see cref="Subdomain"/> as https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address, honoring the <see cref="BaseUrl"/> override.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl!.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
