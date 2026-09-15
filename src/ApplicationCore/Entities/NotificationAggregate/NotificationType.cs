namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>The order event a notification corresponds to.</summary>
public enum NotificationType
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    /// <summary>The "how did the delivery go?" follow-up, queued with the provider for a few days after dispatch.</summary>
    DeliveryFeedback = 3,
    OrderCancelled = 4,
    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 5
}
