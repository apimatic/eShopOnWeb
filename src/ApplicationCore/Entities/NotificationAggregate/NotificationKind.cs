namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason a message was sent to a shopper as their order moved through its lifecycle.
/// </summary>
public enum NotificationKind
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
