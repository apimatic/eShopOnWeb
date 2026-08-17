namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given <see cref="OrderNotification"/> was sent.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 2,

    /// <summary>Scheduled "How did the delivery go?" follow-up, queued with the provider.</summary>
    DeliveryFollowUp = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
