namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>The reason a message about an order was sent.</summary>
public enum OrderNotificationType
{
    /// <summary>The order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>The order was dispatched ("on its way").</summary>
    OrderDispatched = 1,

    /// <summary>The delivery follow-up ("how did it go?"), scheduled with the provider for later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>The order was cancelled.</summary>
    OrderCancelled = 3
}
