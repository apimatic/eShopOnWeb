namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A catalog item and the quantity of it to order.</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
