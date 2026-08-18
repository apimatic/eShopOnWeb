namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an SMS was sent to a shopper about one of their orders.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when the order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when the order is dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFeedback = 2,

    /// <summary>Sent when the order is cancelled.</summary>
    OrderCancelled = 3
}
