namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Added to support operator-driven
/// dispatch/cancel actions and the SMS notifications that accompany them.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
