namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order lifecycle event a notification corresponds to.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent immediately after an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent immediately when an order is marked dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>A "how did the delivery go?" message queued with the provider for a few days later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent immediately when an order is cancelled.</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
