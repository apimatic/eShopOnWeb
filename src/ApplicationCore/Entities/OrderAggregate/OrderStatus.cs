namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its lifecycle. Added so the shop has a notion of an order having been
/// dispatched or cancelled — something the original checkout flow did not track.
/// </summary>
public enum OrderStatus
{
    Placed = 1,
    Dispatched = 2,
    Cancelled = 3
}
