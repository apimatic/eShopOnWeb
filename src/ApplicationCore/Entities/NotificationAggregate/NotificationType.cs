namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of message an <see cref="OrderNotification"/> represents as an order moves.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent when the order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator dispatches the order.</summary>
    OrderDispatched = 1,

    /// <summary>
    /// A "how did the delivery go?" message scheduled with the provider for a few days after
    /// dispatch. Called off if the order is cancelled before it goes out.
    /// </summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3
}
