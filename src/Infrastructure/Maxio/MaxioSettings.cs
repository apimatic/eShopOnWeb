using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values are secrets and must come from
/// user-secrets, environment variables (<c>Maxio__ApiKey</c>) or a secret store - never from a
/// file in source control.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio Advanced Billing API key, used as the HTTP Basic user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, e.g. <c>acme-sandbox</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are published as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional. When set it is used verbatim as the API base address; otherwise the base address
    /// is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address: <see cref="BaseUrl"/> when supplied, otherwise derived from the
    /// site subdomain.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            if (!Uri.TryCreate(EnsureTrailingSlash(trimmed), UriKind.Absolute, out var explicitUri))
            {
                throw new FormatException($"Maxio:BaseUrl is not a valid absolute URL: '{BaseUrl}'.");
            }

            return explicitUri;
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/");
    }

    // HttpClient only resolves relative request URIs against a base address that ends in '/'.
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
