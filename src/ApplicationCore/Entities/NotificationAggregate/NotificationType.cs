namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order-lifecycle event a notification message corresponds to.
/// </summary>
public enum NotificationType
{
    /// <summary>The order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>The order was dispatched / is on its way.</summary>
    OrderDispatched = 1,

    /// <summary>A follow-up asking how the delivery went, queued with the provider for a few days later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>The order was cancelled.</summary>
    OrderCancelled = 3
}
