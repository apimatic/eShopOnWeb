using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Walmart;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the Walmart Marketplace Inventory API. The concrete implementation talks to
/// Walmart strictly through the contract in <c>api-specs/walmart</c> (authorization + inventory).
/// Everything above this interface deals only in eShop seller SKUs and plain quantities.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>
    /// Sets Walmart's own recorded quantity for a single seller SKU to <paramref name="quantity"/>.
    /// </summary>
    Task<WalmartInventorySetResult> SetInventoryAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the quantity Walmart currently records for a single seller SKU, or <c>null</c> when
    /// Walmart holds no inventory record for that SKU.
    /// </summary>
    Task<WalmartInventorySnapshot?> GetInventoryAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a batch of SKU quantities to Walmart in as few calls as the contract allows, reporting
    /// per-SKU whether Walmart accepted the update.
    /// </summary>
    Task<IReadOnlyList<WalmartSyncResult>> SyncInventoryAsync(IReadOnlyCollection<WalmartInventoryUpdate> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every SKU Walmart currently holds an inventory record for, following pagination
    /// to completion.
    /// </summary>
    Task<IReadOnlyList<WalmartInventorySnapshot>> ListInventoryAsync(CancellationToken cancellationToken = default);
}
