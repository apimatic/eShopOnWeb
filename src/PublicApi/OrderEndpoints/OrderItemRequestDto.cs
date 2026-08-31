namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A requested catalog item and quantity for a new order.</summary>
public class OrderItemRequestDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
