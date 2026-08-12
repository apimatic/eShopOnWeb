namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>Which order event a notification was raised for.</summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
