namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a notification was sent as an order moved through its lifecycle.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    Dispatched = 1,

    /// <summary>"Your order was cancelled."</summary>
    Cancelled = 2,

    /// <summary>The scheduled "how did the delivery go?" follow-up, queued with the provider.</summary>
    DeliveryFollowUp = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
