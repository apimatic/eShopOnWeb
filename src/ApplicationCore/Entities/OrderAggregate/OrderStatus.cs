namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. The classic eShop checkout only ever
/// produced <see cref="Placed"/> orders; the SMS-notification feature adds the
/// operator-driven <see cref="Dispatched"/> and <see cref="Cancelled"/> transitions.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
