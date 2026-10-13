using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Talks to Walmart Marketplace's Inventory API on behalf of eShop. Every method maps to an
/// operation in the Walmart OpenAPI contract; implementations own authentication (OAuth 2.0
/// client-credentials) and base-URL resolution.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>
    /// Sets Walmart's own recorded quantity for a single SKU to <paramref name="quantity"/>.
    /// Returns the quantity Walmart records afterwards.
    /// </summary>
    Task<WalmartInventoryRecord> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads Walmart's own current record for a single SKU. Returns <c>null</c> when Walmart holds
    /// no record for the SKU (HTTP 404 from the contract).
    /// </summary>
    Task<WalmartInventoryRecord?> GetStockAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes an absolute stock level for many SKUs to Walmart in as few calls as the contract
    /// allows, reporting per-SKU success or failure.
    /// </summary>
    Task<WalmartBulkResult> SyncStockAsync(IReadOnlyList<WalmartInventoryUpdate> updates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every SKU Walmart currently holds stock for, following the contract's pagination to
    /// completion.
    /// </summary>
    Task<IReadOnlyList<WalmartInventoryRecord>> ListStockAsync(CancellationToken cancellationToken = default);
}
