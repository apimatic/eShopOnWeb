using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class NotificationMessages
{
    public static string OrderPlaced(Order order) =>
        $"eShop: your order #{order.Id} has been placed. Total: {order.Total():C}. Thank you for shopping with us!";

    public static string OrderDispatched(Order order) =>
        $"eShop: good news — your order #{order.Id} is on its way!";

    public static string DeliveryFollowUp(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";

    public static string OrderCancelled(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
}
