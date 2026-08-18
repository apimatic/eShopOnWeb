namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Additive to the original checkout flow, which only
/// ever created an order and never moved it beyond placement.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
