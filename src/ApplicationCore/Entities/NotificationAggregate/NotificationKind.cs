namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order-lifecycle message a <see cref="Notification"/> represents.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    OrderCancelled = 3,
    /// <summary>The "how did the delivery go?" follow-up, queued with the provider for a few days later.</summary>
    DeliveryFollowUp = 4
}
