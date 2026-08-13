namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a notification was sent — which point in an order's life it corresponds to.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>The follow-up asking how the delivery went, scheduled for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 3
}
