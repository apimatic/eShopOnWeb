using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>A SKU and the quantity recorded against it.</summary>
public record WalmartSkuStock(string Sku, int Quantity);

/// <summary>Per-SKU outcome of a catalog sync. <see cref="Status"/> is <c>updated</c> or <c>failed</c>.</summary>
public record WalmartSyncResultItem(string Sku, string Status);

/// <summary>
/// One SKU in the Walmart-vs-eShop comparison. A quantity is <c>null</c> on the side that has no such SKU.
/// </summary>
public record WalmartStockComparisonItem(string Sku, int? WalmartQuantity, int? EshopQuantity);

/// <summary>The full comparison. <see cref="Partial"/> is true when the Walmart listing was cut short at the page cap.</summary>
public record WalmartStockReport(IReadOnlyList<WalmartStockComparisonItem> Items, bool Partial);

/// <summary>
/// The shop's view of its catalog's stock on Walmart Marketplace. Every method talks to Walmart through the
/// wm-v4 SDK; failures surface as <see cref="WalmartIntegrationException"/>.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>Sets how many of the given SKU the shop offers on Walmart (absolute). Returns what Walmart now records.</summary>
    Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken);

    /// <summary>Reads the SKU's quantity as Walmart currently records it (a live read, not a local copy).</summary>
    Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken);

    /// <summary>Pushes eShop's own stock level for every catalog item to Walmart, reporting per-SKU what Walmart did.</summary>
    Task<IReadOnlyList<WalmartSyncResultItem>> SyncCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Lists every SKU Walmart holds stock for, lined up against eShop's own level for the matching catalog item.</summary>
    Task<WalmartStockReport> GetStockComparisonAsync(CancellationToken cancellationToken);
}
