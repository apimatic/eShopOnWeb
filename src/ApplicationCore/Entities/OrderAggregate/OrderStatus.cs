namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Added to support SMS notifications as an order
/// moves. The existing checkout flow creates orders in the <see cref="Placed"/> state; operators
/// advance them to <see cref="Dispatched"/> or <see cref="Cancelled"/>.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
