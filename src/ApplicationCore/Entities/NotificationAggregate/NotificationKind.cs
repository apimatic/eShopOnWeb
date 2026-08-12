namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>Which order event a notification was raised for.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
