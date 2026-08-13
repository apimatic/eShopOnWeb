namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order-progress message an <see cref="OrderNotification"/> represents.
/// </summary>
public enum NotificationType
{
    /// <summary>Sent immediately when an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent immediately when an order is dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>Queued with the provider for a few days after dispatch, asking how delivery went.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent immediately when an order is cancelled.</summary>
    OrderCancelled = 3
}
