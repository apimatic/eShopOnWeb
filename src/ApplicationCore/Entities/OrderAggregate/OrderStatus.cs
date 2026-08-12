namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its fulfilment lifecycle. Orders start <see cref="Placed"/> at checkout
/// and an operator moves them to <see cref="Dispatched"/> or <see cref="Cancelled"/>.
/// </summary>
public enum OrderStatus
{
    Placed = 1,
    Dispatched = 2,
    Cancelled = 3
}
