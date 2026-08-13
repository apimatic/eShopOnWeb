namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// One requested order line: a catalog item and how many of it. Used when placing an order from
/// catalog item ids and quantities.
/// </summary>
public class OrderLineRequest
{
    public OrderLineRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}
