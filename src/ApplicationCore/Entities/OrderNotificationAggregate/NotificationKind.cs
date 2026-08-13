namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Which order event a notification represents.
/// </summary>
public enum NotificationKind
{
    /// <summary>Sent when an order is placed.</summary>
    OrderPlaced = 0,

    /// <summary>Sent when an order is dispatched.</summary>
    OrderDispatched = 1,

    /// <summary>A "how did the delivery go?" message scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Sent when an order is cancelled.</summary>
    OrderCancelled = 3
}
