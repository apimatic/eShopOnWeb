namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public static class NotificationKinds
{
    public const string OrderPlaced = "OrderPlaced";
    public const string OrderDispatched = "OrderDispatched";
    public const string DeliveryFollowUp = "DeliveryFollowUp";
    public const string OrderCancelled = "OrderCancelled";
    public const string Resend = "Resend";
}
