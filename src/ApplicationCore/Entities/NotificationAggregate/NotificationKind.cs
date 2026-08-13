namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an SMS notification was raised for an order.
/// </summary>
public enum NotificationKind
{
    /// <summary>The order was placed by the shopper.</summary>
    OrderPlaced = 1,

    /// <summary>The order was marked dispatched by an operator.</summary>
    OrderDispatched = 2,

    /// <summary>The delayed "how did the delivery go?" follow-up, scheduled with the provider.</summary>
    DeliveryFollowUp = 3,

    /// <summary>The order was cancelled by an operator.</summary>
    OrderCancelled = 4
}
