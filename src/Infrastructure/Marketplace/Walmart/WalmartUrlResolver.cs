using System;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Builds absolute request URIs for Walmart calls from a contract server address plus a path,
/// applying the optional base-URL override. The override replaces only the scheme, host and port;
/// the full path (including the contract's base path and any query) is preserved exactly.
/// </summary>
internal sealed class WalmartUrlResolver
{
    private readonly Uri? _override;

    public WalmartUrlResolver(string? baseUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(baseUrlOverride))
        {
            if (!Uri.TryCreate(baseUrlOverride.Trim(), UriKind.Absolute, out var parsed))
            {
                throw new ArgumentException(
                    $"Walmart:BaseUrl must be an absolute URL of the form scheme://host[:port]; got '{baseUrlOverride}'.",
                    nameof(baseUrlOverride));
            }
            _override = parsed;
        }
    }

    /// <summary>
    /// Combines a contract server address (e.g. <c>https://marketplace.walmart.com/seller/v1</c>)
    /// with a contract path (e.g. <c>/inventory/v4/items/ESHOP-7</c>) into the full URI to call,
    /// substituting the override's scheme/host/port when one is configured.
    /// </summary>
    public Uri Resolve(string contractServer, string path)
    {
        var full = new Uri(contractServer.TrimEnd('/') + path, UriKind.Absolute);

        if (_override is null)
        {
            return full;
        }

        var builder = new UriBuilder(full)
        {
            Scheme = _override.Scheme,
            Host = _override.Host,
            Port = _override.Port
        };
        return builder.Uri;
    }
}
