namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an <see cref="Order"/> as it moves through fulfilment. Added to support order
/// notifications; existing checkout continues to create orders in the <see cref="Placed"/> state.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Canceled = 2
}
