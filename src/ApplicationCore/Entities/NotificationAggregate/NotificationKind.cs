namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason a notification was raised as an order moved through its lifecycle.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3,
    OrderCancelled = 4,
    /// <summary>An operator-initiated re-send of a message that did not reach the shopper.</summary>
    Resend = 5
}
