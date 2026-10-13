namespace Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;

public class SetCatalogItemStockRequest : BaseRequest
{
    /// <summary>The absolute number of units eShop holds for this item. Must be zero or greater.</summary>
    public int Quantity { get; set; }
}
