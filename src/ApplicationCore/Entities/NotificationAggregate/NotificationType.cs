namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a notification was sent. Mirrors the points in an order's life at which the shopper is told
/// something, plus the delivery follow-up and operator re-sends.
/// </summary>
public enum NotificationType
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 2,

    /// <summary>"How did the delivery go?" — scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3,

    /// <summary>An operator-initiated re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
