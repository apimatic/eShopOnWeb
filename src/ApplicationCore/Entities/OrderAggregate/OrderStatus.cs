namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an <see cref="Order"/> for the purpose of shopper notifications.
/// The base eShopOnWeb order flow only ever created an order; dispatch and cancel are
/// additive states introduced by the SMS notification capability.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
