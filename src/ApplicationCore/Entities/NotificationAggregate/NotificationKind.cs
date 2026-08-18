namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The point in an order's life a notification marks. Determines the message text and,
/// for <see cref="DeliveryFollowUp"/>, that the message is queued with the provider for
/// a future send rather than dispatched immediately.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    DeliveryFollowUp = 3,
    OrderCancelled = 4,
    Resend = 5
}
