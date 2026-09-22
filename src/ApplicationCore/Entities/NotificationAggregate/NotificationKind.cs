namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>Why a notification was sent, as an order moved through its lifecycle.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did the delivery go?" follow-up, scheduled a few days after dispatch.</summary>
    DeliveryFeedback = 2,
    OrderCancelled = 3
}
