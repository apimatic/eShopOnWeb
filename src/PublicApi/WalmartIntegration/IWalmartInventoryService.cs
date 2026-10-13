using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.WalmartIntegration;

/// <summary>
/// The eShop-facing seam over the Walmart Marketplace inventory API. Every method translates SDK
/// failures into a <see cref="WalmartApiException"/> (or, for sync, a per-SKU failed status) so
/// callers never see raw SDK exception types.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>Sets how many of the SKU the shop offers on Walmart (absolute quantity).</summary>
    Task SetStockAsync(string sku, int quantity, CancellationToken cancellationToken);

    /// <summary>Returns the SKU's quantity as Walmart currently records it.</summary>
    Task<int> GetStockAsync(string sku, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes the eShop stock level of every catalog item to Walmart, returning one result per SKU.
    /// </summary>
    Task<IReadOnlyList<SkuSyncResult>> SyncCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Lists every SKU Walmart currently holds stock for, walking all pages.</summary>
    Task<IReadOnlyList<WalmartSkuStock>> ListWalmartStockAsync(CancellationToken cancellationToken);
}
