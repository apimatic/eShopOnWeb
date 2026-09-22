namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of message sent to a shopper as an order moves.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an order is dispatched.</summary>
    Dispatched = 1,

    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for a few days later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an order is cancelled.</summary>
    Cancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
