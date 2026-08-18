namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// Which point in an order's life a notification marks.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent when the order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when the order is dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>Scheduled at dispatch to go out a few days later asking how the delivery went.</summary>
    DeliveryFeedback = 2,

    /// <summary>Sent when the order is cancelled.</summary>
    OrderCancelled = 3
}
