namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>The order event that a notification was raised for.</summary>
public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFeedback,
    OrderCancelled
}
