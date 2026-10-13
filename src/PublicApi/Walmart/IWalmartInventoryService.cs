using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// The integration's seam over the Walmart Marketplace inventory API. All methods translate SDK
/// failures to <see cref="WalmartIntegrationException"/> and bound each call with a deadline.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>Sets Walmart's recorded quantity for a SKU (Flow 1 — <c>UpdateInventoryForSku</c>).</summary>
    Task<WalmartStockLevel> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken);

    /// <summary>Reads Walmart's current recorded quantity for a SKU (Flow 1 — <c>GetInventoryForSku</c>).</summary>
    Task<WalmartStockLevel> GetStockAsync(string sku, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes a batch of SKU quantities to Walmart (Flow 2 — <c>BulkUpdateInventory</c>, chunked),
    /// returning one result per SKU supplied.
    /// </summary>
    Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyCollection<WalmartSkuQuantity> items, CancellationToken cancellationToken);

    /// <summary>
    /// Lists every SKU Walmart holds stock for, walking all pages (Flow 3 —
    /// <c>GetInventorySummaries</c>), capped so the walk cannot run unbounded.
    /// </summary>
    Task<WalmartStockListing> ListAllAsync(CancellationToken cancellationToken);
}
