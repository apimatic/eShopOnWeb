namespace Microsoft.eShopWeb.ApplicationCore.Marketplace;

/// <summary>
/// A SKU and the quantity Walmart records for it. Used both as the input to a sync
/// (the level eShop wants Walmart to hold) and as the output of a read (the level Walmart
/// currently holds).
/// </summary>
public record WalmartSkuStock(string Sku, int Quantity);

/// <summary>
/// The outcome of pushing a single SKU's quantity to Walmart during a sync. <see cref="Success"/>
/// maps to a <c>status</c> of <c>updated</c>; otherwise <c>failed</c>, with the Walmart-reported
/// error code/message when available.
/// </summary>
public record WalmartSyncResult(string Sku, bool Success, string? ErrorCode = null, string? ErrorMessage = null);
