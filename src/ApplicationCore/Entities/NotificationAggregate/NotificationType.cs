namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Which order-lifecycle message a <see cref="Notification"/> represents.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3,
    OrderCancelled = 4
}
