namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Which message in the order lifecycle a notification represents.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
