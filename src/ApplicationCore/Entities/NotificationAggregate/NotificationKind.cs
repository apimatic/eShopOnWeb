namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a notification was sent to a shopper.
/// </summary>
public enum NotificationKind
{
    /// <summary>The order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>The order has been dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>A follow-up, scheduled for a few days after dispatch, asking how the delivery went.</summary>
    DeliveryFollowUp = 2,

    /// <summary>The order was cancelled.</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-sent an earlier message that did not reach the shopper.</summary>
    Resend = 4
}
