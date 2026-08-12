namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason a notification was raised as an order moved through its lifecycle.
/// A resend keeps the <see cref="NotificationType"/> of the message it re-sends.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The follow-up "how did the delivery go?" message, scheduled with the provider.</summary>
    DeliveryFeedbackRequest = 2,
    OrderCancelled = 3
}
