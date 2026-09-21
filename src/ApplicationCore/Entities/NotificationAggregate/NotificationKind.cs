namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason a notification message was produced as an order moves through its lifecycle.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when the shopper places the order.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an operator marks the order dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>The "how did the delivery go" follow-up, queued with the provider for a few days later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an operator cancels the order.</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
