namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderNotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DispatchFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
