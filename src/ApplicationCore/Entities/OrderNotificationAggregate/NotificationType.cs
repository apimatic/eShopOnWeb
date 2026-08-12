namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Why a given SMS was sent about an order.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>Queued with the provider to go out a few days after dispatch, asking how the delivery went.</summary>
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
