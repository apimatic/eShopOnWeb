using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied via .NET user-secrets / environment configuration and are never committed.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username (password is the literal "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (e.g. <c>cp-exp-4</c>). Used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the Maxio product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim (a trailing slash is ensured so
    /// relative request paths resolve correctly); otherwise the address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the absolute base <see cref="Uri"/> for Maxio API calls.</summary>
    public Uri ResolveBaseAddress()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl.Trim();

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
