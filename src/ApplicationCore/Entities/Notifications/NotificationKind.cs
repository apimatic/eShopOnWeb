namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// The reason a notification was created — i.e. which point in the order's life it marks.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>The delayed "how did the delivery go?" follow-up, scheduled with the provider.</summary>
    DeliveryFollowUp = 2,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 3
}
