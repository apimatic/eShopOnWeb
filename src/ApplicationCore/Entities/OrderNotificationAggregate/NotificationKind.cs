namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Which order event a notification was raised for.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    Dispatched = 2,
    DeliveryFollowUp = 3,
    Cancelled = 4
}
