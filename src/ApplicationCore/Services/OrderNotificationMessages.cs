using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the SMS body for each order-progress message. Bodies deliberately contain no phone number
/// or other personal data beyond the order number.
/// </summary>
internal static class OrderNotificationMessages
{
    public static string Placed(Order order) =>
        $"eShop: thanks! Your order #{order.Id} has been placed (total ${order.Total():0.00}). We'll text you when it ships.";

    public static string Dispatched(Order order) =>
        $"eShop: good news — your order #{order.Id} is on its way!";

    public static string DeliveryFollowUp(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

    public static string Cancelled(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. If this wasn't expected, please contact support.";
}
