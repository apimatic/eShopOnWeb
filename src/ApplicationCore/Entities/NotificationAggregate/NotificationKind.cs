namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order-lifecycle event a notification corresponds to.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when the shopper places the order.</summary>
    OrderPlaced = 1,

    /// <summary>Sent when an operator marks the order dispatched.</summary>
    OrderDispatched = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3,

    /// <summary>
    /// The "how did the delivery go?" follow-up, queued with the provider for a few days
    /// after dispatch and called off if the order is cancelled first.
    /// </summary>
    DeliveryFollowUp = 4,
}
