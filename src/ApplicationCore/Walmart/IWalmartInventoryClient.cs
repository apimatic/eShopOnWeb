using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>
/// Talks to the Walmart Marketplace Inventory API. This is the only seam through which the
/// application reaches Walmart; its shape follows the Walmart Inventory OpenAPI contract
/// (set/get a single SKU's quantity, bulk update, and list all inventory), decoupled from the
/// wire schema so callers deal in plain quantities.
/// </summary>
public interface IWalmartInventoryClient
{
    /// <summary>
    /// Sets Walmart's recorded quantity for a single seller SKU to <paramref name="quantity"/>.
    /// </summary>
    Task<WalmartInventoryUpdateResult> UpdateQuantityAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the quantity Walmart currently records for a seller SKU, or <c>null</c> if Walmart
    /// holds no inventory record for it.
    /// </summary>
    Task<WalmartInventoryRecord?> GetQuantityAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes quantities for many seller SKUs in one or more bulk requests, reporting per-SKU
    /// outcomes.
    /// </summary>
    Task<IReadOnlyList<WalmartBulkItemResult>> UpdateQuantitiesAsync(IReadOnlyCollection<WalmartInventoryItem> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every SKU Walmart holds an inventory record for, following pagination to the end.
    /// </summary>
    Task<IReadOnlyList<WalmartInventoryRecord>> ListAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>A SKU/quantity pair to push to Walmart.</summary>
public readonly record struct WalmartInventoryItem(string Sku, int Quantity);

/// <summary>Walmart's recorded inventory for a SKU, as read back from the contract.</summary>
public readonly record struct WalmartInventoryRecord(string Sku, int Quantity);

/// <summary>The result of setting a single SKU's quantity on Walmart.</summary>
public readonly record struct WalmartInventoryUpdateResult(string Sku, int Quantity);

/// <summary>The per-SKU outcome of a bulk push to Walmart.</summary>
public readonly record struct WalmartBulkItemResult(string Sku, bool Success, string? ErrorCode = null, string? ErrorMessage = null);
