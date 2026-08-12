namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Which message in the order lifecycle a notification represents.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>The delayed "how did the delivery go?" follow-up, queued with the provider for a few days later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 3
}
