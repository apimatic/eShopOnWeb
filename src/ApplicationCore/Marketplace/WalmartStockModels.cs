namespace Microsoft.eShopWeb.ApplicationCore.Marketplace;

/// <summary>A single SKU's stock as Walmart currently records it.</summary>
public record WalmartSkuStock(string Sku, int Quantity);

/// <summary>Outcome of pushing one SKU to Walmart during a catalog sync.</summary>
public record WalmartSyncItemResult(string Sku, WalmartSyncStatus Status);

public enum WalmartSyncStatus
{
    Updated,
    Failed
}

/// <summary>
/// One line of the reconciliation report: a SKU lined up with Walmart's quantity and eShop's own
/// level for the matching catalog item. Either side is null when that side has no such SKU.
/// </summary>
public record WalmartStockComparisonItem(string Sku, int? WalmartQuantity, int? EshopQuantity);
