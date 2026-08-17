namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The purpose of an SMS notification within the order lifecycle. A message that is re-sent
/// keeps the <see cref="NotificationType"/> of the message it re-sends.
/// </summary>
public enum NotificationType
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    DeliveryFollowUp = 3,
    OrderCancelled = 4
}
