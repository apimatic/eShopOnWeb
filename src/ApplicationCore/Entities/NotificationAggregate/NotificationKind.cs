namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>The reason a notification was raised as an order moved.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    /// <summary>The "how did the delivery go?" follow-up, queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3
}
