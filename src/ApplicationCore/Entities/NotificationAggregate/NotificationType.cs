namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>The order event a notification was raised for.</summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,

    /// <summary>The "how did the delivery go?" message queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3
}
