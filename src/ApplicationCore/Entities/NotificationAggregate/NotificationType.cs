namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order-lifecycle event that a notification was raised for.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent immediately after the order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator dispatches the order.</summary>
    OrderDispatched = 1,

    /// <summary>
    /// Scheduled with the provider for a few days after dispatch, asking how the
    /// delivery went. Cancelled with the provider if the order is cancelled first.
    /// </summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3
}
