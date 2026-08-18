namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>Which order event a notification was raised for.</summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>The follow-up asking how the delivery went, scheduled a few days after dispatch.</summary>
    DeliveryFollowUp = 2,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
