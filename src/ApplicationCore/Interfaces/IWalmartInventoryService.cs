using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Talks to Walmart Marketplace about how much of the eShop catalog is on offer. Every method
/// translates any transport/API failure into
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.WalmartInventoryException"/>.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>
    /// Sets Walmart's own record of the SKU's quantity to <paramref name="quantity"/>. Returns the
    /// SKU and quantity now recorded. Does not change eShop's own level for the item.
    /// </summary>
    Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>The SKU's stock as Walmart currently records it (read back from Walmart, not a local copy).</summary>
    Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes every supplied catalog item's eShop level to Walmart in one go. Returns one result
    /// per SKU reporting whether Walmart accepted it.
    /// </summary>
    Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyList<(int CatalogItemId, int Quantity)> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every SKU Walmart holds stock for, with Walmart's recorded quantity. The caller lines
    /// these up against eShop's own levels. Covers all SKUs, not only the first page.
    /// </summary>
    Task<IReadOnlyList<WalmartSkuStock>> ListAllStockAsync(CancellationToken cancellationToken = default);
}
