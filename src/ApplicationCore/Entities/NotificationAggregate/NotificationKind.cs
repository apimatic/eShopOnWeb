namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The reason an SMS notification was produced for an order.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFollowUp = 3,
    Resend = 4
}
