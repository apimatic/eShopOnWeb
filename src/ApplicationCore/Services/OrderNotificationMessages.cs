namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The text of the messages sent as an order moves. Kept free of any personal contact detail — only
/// the order reference appears in the body.
/// </summary>
public static class OrderNotificationMessages
{
    public static string OrderPlaced(int orderId) =>
        $"eShopOnWeb: thanks! Your order #{orderId} has been placed and is being prepared.";

    public static string OrderDispatched(int orderId) =>
        $"eShopOnWeb: good news — your order #{orderId} is on its way.";

    public static string DeliveryFollowUp(int orderId) =>
        $"eShopOnWeb: how did the delivery of your order #{orderId} go? We'd love your feedback.";

    public static string OrderCancelled(int orderId) =>
        $"eShopOnWeb: your order #{orderId} has been cancelled. If this is unexpected, please contact us.";
}
