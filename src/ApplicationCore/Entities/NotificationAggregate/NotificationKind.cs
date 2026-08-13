namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given SMS was sent to a shopper about an order.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    /// <summary>The "how did the delivery go?" message queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3,
    OrderCancelled = 4,
    /// <summary>An operator-triggered re-send of a message that did not reach the shopper.</summary>
    Resend = 5
}
