namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order-lifecycle message an <see cref="OrderNotification"/> represents.
/// </summary>
public enum OrderNotificationType
{
    OrderPlaced = 1,
    Dispatched = 2,
    DeliveryFollowUp = 3,
    Cancelled = 4,
    Resend = 5
}
