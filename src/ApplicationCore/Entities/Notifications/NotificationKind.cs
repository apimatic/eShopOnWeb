namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>The reason a notification was sent to a shopper.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did the delivery go?" message, queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
