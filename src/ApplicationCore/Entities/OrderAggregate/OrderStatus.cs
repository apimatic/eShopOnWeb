namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its lifecycle. Added so the shop has a notion of an order having been dispatched
/// or cancelled — the states the SMS notifications are tied to.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
