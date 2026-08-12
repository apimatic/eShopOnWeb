namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. Orders are created as <see cref="Placed"/>
/// and can subsequently be <see cref="Dispatched"/> or <see cref="Cancelled"/> by an operator.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
