namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>The order event a notification was sent for.</summary>
public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
