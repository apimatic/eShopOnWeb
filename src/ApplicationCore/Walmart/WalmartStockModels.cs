namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>A SKU and the quantity recorded for it (on eShop's side or Walmart's).</summary>
public record WalmartSkuStock(string Sku, int Quantity);

/// <summary>The outcome of pushing one SKU's level to Walmart during a sync.</summary>
public record WalmartSyncItemResult(string Sku, string Status)
{
    public const string StatusUpdated = "updated";
    public const string StatusFailed = "failed";

    public static WalmartSyncItemResult Updated(string sku) => new(sku, StatusUpdated);
    public static WalmartSyncItemResult Failed(string sku) => new(sku, StatusFailed);
}

/// <summary>eShop's own stock level for a catalog item, with the SKU it maps to on Walmart.</summary>
public record CatalogItemStockLevel(int CatalogItemId, string Sku, int Quantity);
