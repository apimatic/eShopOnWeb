namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>A catalog item and how many units of it to order.</summary>
public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }

    public OrderLineRequest() { }

    public OrderLineRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }
}
