namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public enum OrderNotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
