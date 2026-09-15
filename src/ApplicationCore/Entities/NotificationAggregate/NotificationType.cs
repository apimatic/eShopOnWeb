namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order event a notification was raised for.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    OrderCancelled = 3,
    DeliveryFollowUp = 4
}
