namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>What a <see cref="Notification"/> was for.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    OrderCancelled = 2,
    DeliveryFollowUp = 3,
    Resend = 4
}
