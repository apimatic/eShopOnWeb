using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>A SKU and the quantity Walmart records for it (null when not available).</summary>
public sealed record WalmartStockLevel(string Sku, int? Quantity);

/// <summary>A SKU and the quantity to push to Walmart.</summary>
public sealed record WalmartSkuQuantity(string Sku, int Quantity);

/// <summary>Per-SKU outcome of a bulk sync: <c>updated</c> or <c>failed</c>.</summary>
public sealed record WalmartSyncItemResult(string Sku, string Status)
{
    public const string Updated = "updated";
    public const string Failed = "failed";
}

/// <summary>
/// The full set of stock levels Walmart holds, plus whether the listing was cut short by the
/// page cap (so the caller learns the result is partial without reading logs).
/// </summary>
public sealed record WalmartStockListing(IReadOnlyList<WalmartStockLevel> Items, bool Truncated);
