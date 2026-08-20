namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum OrderNotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
