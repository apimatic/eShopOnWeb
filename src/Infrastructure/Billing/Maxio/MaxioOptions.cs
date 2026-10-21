using System;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The <c>Maxio:</c> configuration section. Values come from user-secrets in development and from the
/// host's secret store / environment (<c>Maxio__ApiKey</c>, ...) elsewhere — never from a tracked file.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (HTTP Basic user name; the password is "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site, e.g. <c>acme</c> for <c>https://acme.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose plans this shop offers.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> — optional. When set it is used verbatim as the API base address instead of
    /// the one derived from the subdomain and region.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Checks every value the client needs and throws, naming the key, when one is missing or invalid.
    /// Never echoes a value.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw Missing("ApiKey");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw Missing("ProductFamilyHandle");
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:BaseUrl must be an absolute http(s) URL when it is set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw Missing("Subdomain");
        }

        _ = ResolveEnvironment();
    }

    /// <summary>The region; <c>US</c> when <c>Maxio:Environment</c> is not set.</summary>
    public ServerEnvironment ResolveEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment))
            return ServerEnvironment.Us;
        if (ServerEnvironment.TryGetKnownValue(Environment.Trim().ToUpperInvariant(), out var environment))
            return environment;
        throw new InvalidOperationException($"{SectionName}:Environment must be US or EU when it is set.");
    }

    /// <summary>The key the request limiter counts under: the site, or the base URL's host when only that is set.</summary>
    public string RequestLimitKey =>
        !string.IsNullOrWhiteSpace(Subdomain) ? Subdomain!.Trim() : new Uri(BaseUrl!).Host;

    private static InvalidOperationException Missing(string key) =>
        new($"{SectionName}:{key} is not configured. Set it with user-secrets (development) or the host's " +
            $"secret store / environment variable {SectionName}__{key} before starting the app.");
}
