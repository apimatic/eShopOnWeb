using System;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Resolves the effective base addresses for Walmart calls from the contract's server URLs and the
/// optional <c>Walmart:BaseUrl</c> override.
///
/// Each Walmart contract declares its own production server URL (and thus its own base path):
/// the Authorization API is served from <c>.../seller</c> and the Inventory API from
/// <c>.../seller/v1</c>. When an override is supplied it replaces only the scheme/host/port;
/// everything after the host — including each contract's base path — is preserved exactly.
/// </summary>
public static class WalmartEndpoints
{
    // Production server URLs taken verbatim from the OpenAPI contracts (the authoritative source).
    // api-specs/walmart/authorization.json -> servers[0].url
    public const string AuthServerUrl = "https://marketplace.walmart.com/seller";
    // api-specs/walmart/inventory.json -> servers[0].url
    public const string InventoryServerUrl = "https://marketplace.walmart.com/seller/v1";

    public static Uri ResolveAuthBase(string? baseUrlOverride) => ResolveBase(AuthServerUrl, baseUrlOverride);

    public static Uri ResolveInventoryBase(string? baseUrlOverride) => ResolveBase(InventoryServerUrl, baseUrlOverride);

    /// <summary>
    /// Produces an <see cref="Uri"/> suitable as an <see cref="System.Net.Http.HttpClient.BaseAddress"/>
    /// (always trailing-slash terminated) by combining the override's authority (when present) with
    /// the contract server's path.
    /// </summary>
    internal static Uri ResolveBase(string contractServerUrl, string? baseUrlOverride)
    {
        var contract = new Uri(contractServerUrl, UriKind.Absolute);

        string authority;
        if (string.IsNullOrWhiteSpace(baseUrlOverride))
        {
            authority = contract.GetLeftPart(UriPartial.Authority);
        }
        else
        {
            var over = new Uri(baseUrlOverride.Trim(), UriKind.Absolute);
            // GetLeftPart(Authority) yields scheme://host[:port], omitting a default port — exactly
            // the scheme/host/port replacement the override specifies.
            authority = over.GetLeftPart(UriPartial.Authority);
        }

        var path = contract.AbsolutePath.TrimEnd('/');
        return new Uri($"{authority}{path}/", UriKind.Absolute);
    }
}
