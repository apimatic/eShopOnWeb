using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via .NET user-secrets
/// (locally) or environment/App Service configuration in deployed environments - never
/// hard-coded, since the same build must run against different Maxio sites and catalogs.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>
    /// The Maxio Advanced Billing API key for the target site. Sent as the Basic Auth username.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, used to derive the API base address
    /// unless <see cref="BaseUrl"/> is supplied.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The handle of the Product Family that contains the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Maxio API base address. When set, used verbatim instead of
    /// deriving one from <see cref="Subdomain"/> (e.g. for EU-hosted sites).
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseUri()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!
            : $"https://{Subdomain}.chargify.com";

        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(baseUrl, UriKind.Absolute);
    }
}
