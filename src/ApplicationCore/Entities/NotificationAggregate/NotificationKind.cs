namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given SMS notification was raised as an order moved through its lifecycle.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>Delivery follow-up scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
