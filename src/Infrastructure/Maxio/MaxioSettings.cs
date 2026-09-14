using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section. Values are supplied via .NET user-secrets / environment and are never committed.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the HTTP Basic username, with password "x").</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of being derived
    /// from <see cref="Subdomain"/>. Useful for pointing at a different Maxio site or a proxy.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address. Uses <see cref="BaseUrl"/> verbatim when provided,
    /// otherwise derives <c>https://{Subdomain}.chargify.com/</c>. Always ends with a trailing slash
    /// so relative request paths resolve correctly against it.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}.chargify.com"
            : BaseUrl.Trim();

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
