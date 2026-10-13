using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Marketplace;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's port onto Walmart Marketplace inventory. Every method here is a call to
/// Walmart; none of them touch eShop's own data. The concrete implementation lives in the
/// Infrastructure layer and is built against Walmart's OpenAPI contract.
/// </summary>
public interface IWalmartInventoryService
{
    /// <summary>
    /// Sets Walmart's recorded quantity for a single SKU to <paramref name="quantity"/>.
    /// Returns what Walmart recorded for the SKU afterwards.
    /// </summary>
    Task<WalmartSkuStock> SetStockAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the quantity Walmart currently records for a single SKU — the live value from
    /// Walmart, not a copy kept by eShop.
    /// </summary>
    Task<WalmartSkuStock> GetStockAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a quantity for each supplied SKU to Walmart in one operation, returning the
    /// per-SKU outcome.
    /// </summary>
    Task<IReadOnlyList<WalmartSyncResult>> SyncStockAsync(IReadOnlyCollection<WalmartSkuStock> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every SKU Walmart currently holds stock for, with the quantity Walmart records
    /// for each. Pages through the full result set.
    /// </summary>
    Task<IReadOnlyList<WalmartSkuStock>> ListStockAsync(CancellationToken cancellationToken = default);
}
