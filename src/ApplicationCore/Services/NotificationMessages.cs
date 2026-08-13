using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The message bodies sent to shoppers. Kept short and free of anything sensitive — no recipient
/// number, no address — just the order reference and the event.
/// </summary>
public static class NotificationMessages
{
    public static string OrderPlaced(Order order) =>
        $"eShop: your order #{order.Id} for {order.Total():C} has been placed. Thank you for shopping with us!";

    public static string OrderDispatched(Order order) =>
        $"eShop: good news — your order #{order.Id} is on its way!";

    public static string DeliveryFollowUp(Order order) =>
        $"eShop: how did the delivery of your order #{order.Id} go? Reply to let us know — we'd love your feedback.";

    public static string OrderCancelled(Order order) =>
        $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
}
