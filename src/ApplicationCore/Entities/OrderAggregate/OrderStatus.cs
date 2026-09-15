namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in the fulfilment lifecycle. Added so the shop can message the shopper as
/// the order moves and so an operator cannot, for example, dispatch an order that was cancelled.
/// </summary>
public enum OrderStatus
{
    Placed = 1,
    Dispatched = 2,
    Cancelled = 3
}
