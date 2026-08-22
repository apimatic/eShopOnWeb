namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class OrderNotificationKind
{
    public const string OrderPlaced = "OrderPlaced";
    public const string OrderDispatched = "OrderDispatched";
    public const string DeliveryFollowUp = "DeliveryFollowUp";
    public const string OrderCancelled = "OrderCancelled";
}
