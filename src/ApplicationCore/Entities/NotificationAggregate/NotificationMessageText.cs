namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public static class NotificationMessageText
{
    public static string OrderPlaced(int orderId) =>
        $"Your eShop order #{orderId} has been placed.";

    public static string OrderDispatched(int orderId) =>
        $"Your eShop order #{orderId} is on its way.";

    public static string DeliveryFollowUp(int orderId) =>
        $"How did the delivery of eShop order #{orderId} go?";

    public static string OrderCancelled(int orderId) =>
        $"Your eShop order #{orderId} has been cancelled.";
}
