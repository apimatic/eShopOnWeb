using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the text of each order notification from the order and the kind of event. Bodies never
/// contain the recipient's number.
/// </summary>
public static class NotificationMessageComposer
{
    public static string Compose(NotificationType type, Order order) => type switch
    {
        NotificationType.OrderPlaced =>
            $"eShop: Thanks for your order #{order.Id}. We've received it — total {Money(order)}. We'll let you know when it ships.",
        NotificationType.OrderDispatched =>
            $"eShop: Good news! Your order #{order.Id} is on its way.",
        NotificationType.DeliveryFollowUp =>
            $"eShop: How did the delivery of your order #{order.Id} go? We'd love to hear how it went.",
        NotificationType.OrderCancelled =>
            $"eShop: Your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
        _ => $"eShop: Update on your order #{order.Id}."
    };

    private static string Money(Order order) =>
        order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
