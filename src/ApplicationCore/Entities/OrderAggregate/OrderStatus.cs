namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its fulfilment lifecycle. Added so the shop can notify the shopper as
/// the order moves. Existing checkout flows leave orders in the default <see cref="Placed"/> state.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
