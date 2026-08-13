namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of message an order notification represents as an order moves.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    OrderCancelled = 3,
    DeliveryFollowUp = 4
}
