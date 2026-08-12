namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. An order is <see cref="Placed"/> when created,
/// can then be <see cref="Dispatched"/> by an operator, and can be <see cref="Cancelled"/>
/// from either state.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
