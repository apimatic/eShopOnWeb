namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. An order starts life <see cref="Placed"/>
/// and can move on to <see cref="Dispatched"/> or <see cref="Cancelled"/>. These are the only
/// states the SMS notification feature reasons about.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
