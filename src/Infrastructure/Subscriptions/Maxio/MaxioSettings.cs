using System;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Strongly-typed settings bound from the <c>Maxio:</c> configuration section. Values are supplied via
/// user-secrets / environment configuration and must never be hard-coded, so the same build can target a
/// different Maxio site and catalog.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key. Used as the HTTP Basic username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain (e.g. <c>your-site</c>). Used to derive the base URL when <see cref="BaseUrl"/> is unset.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit base URL. When set it is used verbatim; otherwise the base URL is derived from
    /// <see cref="Subdomain"/> per the OpenAPI <c>servers</c> template (<c>https://{site}.chargify.com</c>).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address, honoring the optional <see cref="BaseUrl"/> override.</summary>
    public Uri ResolveBaseUrl()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // HttpClient requires a trailing slash on the base address for relative request URIs to compose correctly.
        if (!raw.EndsWith('/'))
            raw += "/";

        return new Uri(raw, UriKind.Absolute);
    }
}
