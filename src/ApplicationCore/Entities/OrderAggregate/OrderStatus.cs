namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order sits in its fulfilment lifecycle. Introduced so the shop can message the
/// shopper as the order moves (placed -> dispatched, or placed/dispatched -> cancelled).
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
