namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of message that goes out to a shopper as an order moves through its lifecycle.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent immediately after an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator dispatches the order.</summary>
    OrderDispatched = 1,

    /// <summary>Scheduled at dispatch time to go out a few days later, asking how the delivery went.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3
}
