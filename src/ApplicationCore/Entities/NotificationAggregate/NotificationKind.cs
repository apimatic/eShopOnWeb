namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an SMS notification was sent to a shopper about an order.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent immediately when an order is placed.</summary>
    OrderPlaced = 1,

    /// <summary>Sent immediately when an order is dispatched.</summary>
    OrderDispatched = 2,

    /// <summary>
    /// A follow-up asking how the delivery went, scheduled with the provider to go out a few days
    /// after dispatch. Called off if the order is cancelled before it is sent.
    /// </summary>
    DeliveryFollowUp = 3,

    /// <summary>Sent immediately when an order is cancelled.</summary>
    OrderCancelled = 4
}
