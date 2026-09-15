namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an SMS notification was raised as an order moved through its lifecycle.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent when a shopper places an order.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator marks the order dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>Scheduled with the provider for a few days after dispatch, asking how the delivery went.</summary>
    DeliveryFeedbackRequest = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
