namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its fulfilment life. Additive to the original model: an order starts
/// <see cref="Placed"/> and an operator moves it to <see cref="Dispatched"/> or <see cref="Cancelled"/>.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
