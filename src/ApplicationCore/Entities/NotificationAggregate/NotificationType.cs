namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Which order event a notification was raised for.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFollowUp = 3
}
