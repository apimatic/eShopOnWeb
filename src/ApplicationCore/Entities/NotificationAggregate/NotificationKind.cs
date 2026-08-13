namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The order lifecycle event that a notification message corresponds to.
/// </summary>
public enum NotificationKind
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>"How did your delivery go?" — queued with the provider to go out a few days after dispatch.</summary>
    DeliveryFeedback = 2,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 3
}
