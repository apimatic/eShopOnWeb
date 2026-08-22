namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PlaceOrderItem
{
    public PlaceOrderItem(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}
