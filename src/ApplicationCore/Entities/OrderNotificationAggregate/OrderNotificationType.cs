namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>The reason a notification was sent to a shopper.</summary>
public enum OrderNotificationType
{
    /// <summary>The order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>The order was dispatched and is on its way.</summary>
    OrderDispatched = 1,

    /// <summary>The order was cancelled.</summary>
    OrderCancelled = 2,

    /// <summary>A follow-up, queued with the provider for a few days after dispatch,
    /// asking the shopper how the delivery went.</summary>
    DeliveryFollowUp = 3
}
