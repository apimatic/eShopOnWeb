namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The reason an order notification was sent, i.e. which point in the order's life it marks.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    OrderCancelled = 3,
    DeliveryFollowUp = 4
}
