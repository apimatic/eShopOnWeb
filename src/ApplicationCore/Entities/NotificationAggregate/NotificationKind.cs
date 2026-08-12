namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an order notification was sent to a shopper.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when an order is placed.</summary>
    OrderPlaced = 1,

    /// <summary>Sent when an order is marked dispatched.</summary>
    OrderDispatched = 2,

    /// <summary>Scheduled at dispatch time to ask, a few days later, how the delivery went.</summary>
    DeliveryFollowUp = 3,

    /// <summary>Sent when an order is cancelled.</summary>
    OrderCancelled = 4
}
