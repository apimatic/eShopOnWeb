namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The point in an order's life that a notification marks.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent immediately when the order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent immediately when an operator dispatches the order.</summary>
    OrderDispatched = 1,

    /// <summary>Queued with the provider at dispatch time to go out a few days later, asking how the delivery went.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent immediately when an operator cancels the order.</summary>
    OrderCancelled = 3,
}
