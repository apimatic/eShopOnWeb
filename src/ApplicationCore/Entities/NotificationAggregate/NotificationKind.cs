namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The lifecycle event that caused an SMS notification to be created.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 1,
    OrderDispatched = 2,
    DeliveryFollowUp = 3,
    OrderCancelled = 4,
    Resend = 5
}
