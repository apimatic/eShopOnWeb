namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order-progress message a <see cref="Notification"/> represents.
/// </summary>
public enum NotificationType
{
    /// <summary>Told the shopper their order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>Told the shopper their order is on its way.</summary>
    OrderDispatched = 1,

    /// <summary>Follow-up asking how the delivery went (scheduled for later with the provider).</summary>
    DeliveryFeedback = 2,

    /// <summary>Told the shopper their order was cancelled.</summary>
    OrderCancelled = 3
}
