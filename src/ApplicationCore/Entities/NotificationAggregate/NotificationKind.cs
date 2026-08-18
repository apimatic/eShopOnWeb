namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason a notification was created, i.e. which point in an order's life it marks.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    Dispatched = 1,
    DeliveryFollowUp = 2,
    Cancelled = 3
}
