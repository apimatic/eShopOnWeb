namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum OrderNotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFeedback = 2,
    OrderCancelled = 3
}
