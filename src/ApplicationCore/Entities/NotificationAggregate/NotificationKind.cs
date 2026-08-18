namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given SMS was sent. Each notification records the moment in the order lifecycle it
/// belongs to; <see cref="DeliveryFeedback"/> is the "how did the delivery go?" follow-up that is
/// scheduled with the provider when an order is dispatched.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFeedback = 3
}
