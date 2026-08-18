namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. An order starts <see cref="Placed"/>; an operator
/// may move it to <see cref="Dispatched"/> or <see cref="Cancelled"/>. These transitions are what
/// drive the SMS notifications sent to the shopper.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
