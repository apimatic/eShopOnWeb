namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an <see cref="Order"/>. Orders start <see cref="Placed"/> at checkout and can
/// then be <see cref="Dispatched"/> or <see cref="Cancelled"/> by an operator.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
