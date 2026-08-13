namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order-lifecycle message a notification represents.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFollowUp = 3
}
