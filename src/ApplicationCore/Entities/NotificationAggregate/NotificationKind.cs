namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>What a notification is about, as an order moves through its lifecycle.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for later.</summary>
    DeliveryFeedback = 2,
    OrderCancelled = 3
}
