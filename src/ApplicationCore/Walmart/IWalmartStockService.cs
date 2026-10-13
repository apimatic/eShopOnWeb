using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>
/// Operator-facing operations that connect eShop's catalog stock with Walmart Marketplace:
/// setting/reading a single SKU's quantity on Walmart, pushing the whole catalog, and
/// reconciling the two sides.
/// </summary>
public interface IWalmartStockService
{
    /// <summary>Sets Walmart's recorded quantity for one seller SKU. Does not change eShop's level.</summary>
    Task<WalmartInventoryUpdateResult> SetWalmartQuantityAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Reads Walmart's own recorded quantity for one seller SKU, or <c>null</c> if Walmart holds no record.</summary>
    Task<WalmartInventoryRecord?> GetWalmartQuantityAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the eShop stock level of every catalog item to Walmart (an item never set counts
    /// as 0), reporting what Walmart did with each SKU.
    /// </summary>
    Task<IReadOnlyList<WalmartSyncResult>> SyncCatalogToWalmartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up every SKU Walmart holds stock for against eShop's level for the matching catalog
    /// item, surfacing SKUs that differ or that exist on only one side.
    /// </summary>
    Task<IReadOnlyList<WalmartReconciliationItem>> ReconcileAsync(CancellationToken cancellationToken = default);
}

public enum WalmartSyncStatus
{
    Updated,
    Failed
}

/// <summary>The outcome of pushing one SKU during a full-catalog sync.</summary>
public readonly record struct WalmartSyncResult(string Sku, WalmartSyncStatus Status, string? ErrorMessage = null);

/// <summary>
/// One SKU in the reconciliation report. A quantity is <c>null</c> on the side that has no such SKU.
/// </summary>
public readonly record struct WalmartReconciliationItem(string Sku, int? WalmartQuantity, int? EshopQuantity);
