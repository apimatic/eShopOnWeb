namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum OrderNotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFollowUp = 3
}
