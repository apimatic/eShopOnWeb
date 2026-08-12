namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given message went out.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
