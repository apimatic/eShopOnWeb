namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class OrderSmsTemplates
{
    public static string Placed(int orderId) =>
        $"eShopOnWeb: your order {orderId} has been placed.";

    public static string Dispatched(int orderId) =>
        $"eShopOnWeb: your order {orderId} is on its way.";

    public static string DeliveryFollowUp(int orderId) =>
        $"eShopOnWeb: how did the delivery of order {orderId} go?";

    public static string Cancelled(int orderId) =>
        $"eShopOnWeb: your order {orderId} has been cancelled.";
}
