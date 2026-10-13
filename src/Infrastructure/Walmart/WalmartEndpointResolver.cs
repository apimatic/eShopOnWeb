using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Builds the absolute URLs for every Walmart call from the OpenAPI contract's server addresses,
/// applying the optional <see cref="WalmartOptions.BaseUrl"/> override. The override replaces only
/// the scheme, host and port; the base path each contract server carries (<c>/seller</c> for
/// authorization, <c>/seller/v1</c> for inventory) and the operation path are preserved exactly.
/// </summary>
public class WalmartEndpointResolver
{
    // Contract server addresses (servers[0]) taken verbatim from the Walmart OpenAPI documents.
    // authorization.json: https://marketplace.walmart.com/seller
    private const string AuthorizationContractServer = "https://marketplace.walmart.com/seller";
    // inventory.json: https://marketplace.walmart.com/seller/v1
    private const string InventoryContractServer = "https://marketplace.walmart.com/seller/v1";

    private readonly Uri? _override;

    public WalmartEndpointResolver(IOptions<WalmartOptions> options)
    {
        var baseUrl = options.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Walmart:BaseUrl must be an absolute URL of the form scheme://host[:port]. Value was '{baseUrl}'.");
            }
            _override = parsed;
        }
    }

    /// <summary>Token endpoint: POST /auth/v4/token (authorization contract).</summary>
    public Uri TokenUrl() => Build(AuthorizationContractServer, "/auth/v4/token");

    /// <summary>Single-SKU inventory: GET/PUT /inventory/v4/items/{sku} (inventory contract).</summary>
    public Uri InventoryItemUrl(string sku) =>
        Build(InventoryContractServer, "/inventory/v4/items/" + Uri.EscapeDataString(sku));

    /// <summary>Bulk inventory update: POST /inventory/v4/items/bulk (inventory contract).</summary>
    public Uri InventoryBulkUrl() => Build(InventoryContractServer, "/inventory/v4/items/bulk");

    /// <summary>Inventory summaries list: GET /inventory/v4/items (inventory contract), with paging query.</summary>
    public Uri InventoryListUrl(int pageSize, string? nextToken)
    {
        var query = new StringBuilder("?pageSize=").Append(pageSize);
        if (!string.IsNullOrEmpty(nextToken))
        {
            query.Append("&nextToken=").Append(Uri.EscapeDataString(nextToken));
        }
        return Build(InventoryContractServer, "/inventory/v4/items" + query);
    }

    private Uri Build(string contractServer, string operationPathAndQuery)
    {
        var contractUrl = new Uri(contractServer.TrimEnd('/') + operationPathAndQuery, UriKind.Absolute);

        if (_override is null)
        {
            return contractUrl;
        }

        // Swap scheme/host/port only; keep path and query exactly as the contract specifies.
        var builder = new UriBuilder(contractUrl)
        {
            Scheme = _override.Scheme,
            Host = _override.Host,
            Port = _override.Port
        };
        return builder.Uri;
    }
}
