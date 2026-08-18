namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Which point in an order's life a notification marks.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when the shopper places the order.</summary>
    OrderPlaced = 1,

    /// <summary>Sent when an operator marks the order dispatched.</summary>
    OrderDispatched = 2,

    /// <summary>A "how did the delivery go?" message scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 4,

    /// <summary>An operator-initiated re-send of a message that did not reach the shopper.</summary>
    Resend = 5
}
