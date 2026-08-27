namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DispatchFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
