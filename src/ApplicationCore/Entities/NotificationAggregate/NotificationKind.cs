namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// What order event a notification was raised for. The kind determines the message text.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent immediately when an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent immediately when an operator dispatches an order.</summary>
    OrderDispatched = 1,

    /// <summary>
    /// Queued with the provider for a few days after dispatch, asking how the delivery went.
    /// Cancelled (at the provider) if the order is cancelled before it goes out.
    /// </summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent immediately when an operator cancels an order.</summary>
    OrderCancelled = 3
}
