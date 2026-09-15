namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Where an order sits in the notification lifecycle. This is additive to the existing
/// order/order-item model and never mutates it.
/// </summary>
public enum OrderProgressStatus
{
    Placed = 0,
    Dispatched = 1,
    Cancelled = 2
}
