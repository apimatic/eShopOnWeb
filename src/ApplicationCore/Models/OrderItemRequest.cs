namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
