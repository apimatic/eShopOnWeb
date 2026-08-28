namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    DeliveryFollowUp = 3,
    OrderCancelled = 4,
    Resend = 5
}
