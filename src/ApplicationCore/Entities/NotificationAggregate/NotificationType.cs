namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order lifecycle moment a notification corresponds to.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    DeliveryFollowUp = 3,
    OrderCancelled = 4,
    Resend = 5
}
