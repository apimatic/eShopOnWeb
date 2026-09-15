namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order-lifecycle event a notification corresponds to.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    /// <summary>The "how was delivery?" follow-up, scheduled with the provider for a few days later.</summary>
    DeliveryFollowUp = 3,
    OrderCancelled = 4
}
