namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an SMS was created for an order.
/// </summary>
public enum NotificationType
{
    /// <summary>The order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>The order was dispatched and is on its way.</summary>
    OrderDispatched = 1,

    /// <summary>A follow-up, scheduled with the provider for a few days after dispatch, asking how the delivery went.</summary>
    DeliveryFollowUp = 2,

    /// <summary>The order was cancelled.</summary>
    OrderCancelled = 3
}
