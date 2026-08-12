using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>The text of each message the shop sends as an order moves.</summary>
public static class OrderNotificationMessages
{
    public static string Placed(Order order) =>
        $"Thanks for shopping with eShop! Your order #{order.Id} has been placed"
        + $" (total {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}). We'll let you know when it ships.";

    public static string Dispatched(Order order) =>
        $"Good news! Your eShop order #{order.Id} is on its way.";

    public static string DeliveryFollowUp(Order order) =>
        $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";

    public static string Cancelled(Order order) =>
        $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
}
