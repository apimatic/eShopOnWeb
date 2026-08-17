namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The kind of order-lifecycle event a notification message is about.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFollowUp = 3
}
