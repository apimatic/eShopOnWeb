namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given <see cref="Notification"/> was raised. This is eShop's own classification of the
/// message; the delivery outcome is tracked separately in <see cref="Notification.ProviderStatus"/>.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator marks the order dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>
    /// The "how did the delivery go?" message, scheduled with the provider for a few days after
    /// dispatch. Cancelled with the provider if the order is cancelled before it goes out.
    /// </summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
