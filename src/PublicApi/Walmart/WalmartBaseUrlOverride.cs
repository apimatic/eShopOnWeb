using System;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Applies the optional <c>Walmart:BaseUrl</c> override (<c>scheme://host[:port]</c>) to a
/// contract URL: it replaces the scheme, host and port while preserving everything after the host
/// (path and query), per the integration's base-URL rule.
/// </summary>
public static class WalmartBaseUrlOverride
{
    /// <summary>
    /// Returns <paramref name="originalUrl"/> with its scheme/host/port swapped for those of
    /// <paramref name="overrideBaseUrl"/>. When <paramref name="overrideBaseUrl"/> is null/blank,
    /// the original is returned unchanged.
    /// </summary>
    public static string Apply(string originalUrl, string? overrideBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(overrideBaseUrl))
        {
            return originalUrl;
        }

        var ovr = new Uri(overrideBaseUrl, UriKind.Absolute);
        var original = new Uri(originalUrl, UriKind.Absolute);

        var builder = new UriBuilder(original)
        {
            Scheme = ovr.Scheme,
            Host = ovr.Host,
            // Uri.Port returns the scheme default when none was specified; render default as -1
            // (UriBuilder omits it) so "https://host" does not force :443 onto the result.
            Port = ovr.IsDefaultPort ? -1 : ovr.Port,
        };

        return builder.Uri.ToString();
    }
}
