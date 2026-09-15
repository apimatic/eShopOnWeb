namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason a message was sent to a shopper. A re-send keeps the same kind as the message it
/// re-sends; resend lineage is tracked separately via <see cref="OrderNotification.ResendOfNotificationId"/>.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
