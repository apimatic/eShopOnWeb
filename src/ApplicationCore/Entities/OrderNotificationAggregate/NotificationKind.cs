namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
