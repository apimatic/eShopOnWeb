namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given SMS was sent to a shopper, in terms of where the order was in its lifecycle.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent when the shopper places an order.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator marks the order dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>A "how did the delivery go?" message scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3,

    /// <summary>A re-send of an earlier message that did not reach the shopper.</summary>
    Resend = 4
}
