namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle status of an <see cref="Order"/>. Additive to the original checkout flow so the
/// shop can notify shoppers as an order progresses.
/// </summary>
public enum OrderStatus
{
    Placed = 1,
    Dispatched = 2,
    Cancelled = 3
}
