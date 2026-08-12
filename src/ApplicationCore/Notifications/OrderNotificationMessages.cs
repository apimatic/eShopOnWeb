using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>The message bodies sent to shoppers as an order moves. Kept in one place so they read consistently.</summary>
public static class OrderNotificationMessages
{
    public static string Placed(Order order) =>
        $"eShopOnWeb: Thanks! We've received your order #{order.Id} for a total of {order.Total():C}. We'll text you when it ships.";

    public static string Dispatched(Order order) =>
        $"eShopOnWeb: Good news - your order #{order.Id} is on its way!";

    public static string DeliveryFollowUp(Order order) =>
        $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love your feedback.";

    public static string Cancelled(Order order) =>
        $"eShopOnWeb: Your order #{order.Id} has been cancelled. If this wasn't expected, please contact support.";
}
