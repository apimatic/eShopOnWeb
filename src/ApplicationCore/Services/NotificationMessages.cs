namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Message text sent to shoppers. Bodies reference the order number only — never a name or number.</summary>
internal static class NotificationMessages
{
    public static string OrderPlaced(int orderId) =>
        $"eShop: your order #{orderId} has been placed. Thanks for shopping with us!";

    public static string OrderDispatched(int orderId) =>
        $"eShop: good news — your order #{orderId} is on its way.";

    public static string DeliveryFollowUp(int orderId) =>
        $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";

    public static string OrderCancelled(int orderId) =>
        $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.";
}
