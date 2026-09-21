namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// What an <see cref="OrderNotification"/> was about as the order moved.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    /// <summary>The "how did the delivery go" survey queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3
}
