using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Walmart;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Talks to Walmart Marketplace about the stock eShop offers there. Every method raises
/// <see cref="Exceptions.WalmartIntegrationException"/> on a provider or transport failure
/// (except <see cref="SyncAsync"/>, which reports per-SKU outcomes instead of failing the batch).
/// </summary>
public interface IWalmartMarketplaceService
{
    /// <summary>Sets Walmart's recorded quantity for a SKU. Returns the quantity now recorded.</summary>
    Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken);

    /// <summary>Reads the SKU's quantity as Walmart currently records it.</summary>
    Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes each desired SKU level to Walmart, one request per SKU, reporting what Walmart did
    /// with each (updated/failed). A failed SKU never aborts the rest.
    /// </summary>
    Task<IReadOnlyList<WalmartSyncItemResult>> SyncAsync(
        IReadOnlyList<WalmartSkuStock> desired, CancellationToken cancellationToken);

    /// <summary>
    /// Every SKU Walmart holds stock for, with its recorded quantity. Walks all pages of the
    /// Walmart inventory summaries — covers all of them, not only the first page.
    /// </summary>
    Task<IReadOnlyList<WalmartSkuStock>> ListAllStockAsync(CancellationToken cancellationToken);
}
