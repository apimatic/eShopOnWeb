namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. An order starts <see cref="Placed"/> when it is
/// created, moves to <see cref="Dispatched"/> when an operator ships it, and can be
/// <see cref="Cancelled"/> from either state.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
