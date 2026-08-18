namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. An order is <see cref="Placed"/> at checkout and moves
/// to <see cref="Dispatched"/> or <see cref="Cancelled"/> through operator actions. These states drive
/// the SMS notifications sent to the shopper.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
