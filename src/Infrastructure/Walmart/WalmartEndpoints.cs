using System;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// The absolute Walmart URLs this integration calls, taken verbatim from the OpenAPI contracts in
/// <c>api-specs/walmart</c> (the <c>servers[0]</c> production base of each document joined with the
/// operation path). <see cref="Resolve"/> applies the optional <c>Walmart:BaseUrl</c> override by
/// swapping only the scheme/host/port and keeping the rest of the path exactly as the contract gives it.
/// </summary>
public sealed class WalmartEndpoints
{
    // authorization.json -> servers[0].url = https://marketplace.walmart.com/seller ; path /auth/v4/token
    private const string TokenUrl = "https://marketplace.walmart.com/seller/auth/v4/token";

    // inventory.json -> servers[0].url = https://marketplace.walmart.com/seller/v1 ; paths /inventory/v4/...
    private const string InventoryBase = "https://marketplace.walmart.com/seller/v1";

    private readonly Uri? _override;

    public WalmartEndpoints(string? baseUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(baseUrlOverride))
        {
            if (!Uri.TryCreate(baseUrlOverride, UriKind.Absolute, out var parsed))
            {
                throw new ArgumentException(
                    $"Walmart:BaseUrl must be an absolute URL of the form scheme://host[:port]; got '{baseUrlOverride}'.",
                    nameof(baseUrlOverride));
            }
            _override = parsed;
        }
    }

    /// <summary>The access-token endpoint (OAuth2 client_credentials).</summary>
    public Uri TokenEndpoint => Resolve(TokenUrl);

    /// <summary>The single-SKU inventory endpoint for GET and PUT.</summary>
    public Uri InventoryItem(string sku) =>
        Resolve($"{InventoryBase}/inventory/v4/items/{Uri.EscapeDataString(sku)}");

    /// <summary>The bulk inventory update endpoint.</summary>
    public Uri BulkInventory() => Resolve($"{InventoryBase}/inventory/v4/items/bulk");

    /// <summary>The inventory summaries list endpoint, with paging query parameters.</summary>
    public Uri InventorySummaries(int pageSize, string? nextToken)
    {
        var url = $"{InventoryBase}/inventory/v4/items?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(nextToken))
        {
            url += $"&nextToken={Uri.EscapeDataString(nextToken)}";
        }
        return Resolve(url);
    }

    /// <summary>
    /// Applies the base-URL override to a contract URL: replaces scheme/host/port, keeps path and query.
    /// </summary>
    public Uri Resolve(string contractUrl)
    {
        var target = new Uri(contractUrl, UriKind.Absolute);
        if (_override is null)
        {
            return target;
        }

        var builder = new UriBuilder(target)
        {
            Scheme = _override.Scheme,
            Host = _override.Host,
            Port = _override.IsDefaultPort ? -1 : _override.Port
        };
        return builder.Uri;
    }
}
