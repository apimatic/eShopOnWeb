namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A catalog item id and quantity requested when placing an order.
/// </summary>
public class OrderItemEntry
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
