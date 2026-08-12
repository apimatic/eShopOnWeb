using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the short SMS bodies sent as an order moves. Bodies never contain the shopper's number or
/// other PII beyond the order reference.
/// </summary>
public static class NotificationMessages
{
    public static string For(NotificationType type, Order order)
    {
        var reference = order.Id;
        return type switch
        {
            NotificationType.OrderPlaced =>
                $"eShopOnWeb: Thanks for your order! Order #{reference} ({Money(order.Total())}) has been placed.",
            NotificationType.OrderDispatched =>
                $"eShopOnWeb: Good news - your order #{reference} is on its way!",
            NotificationType.DeliveryFeedbackRequest =>
                $"eShopOnWeb: How did the delivery of your order #{reference} go? We'd love your feedback.",
            NotificationType.OrderCancelled =>
                $"eShopOnWeb: Your order #{reference} has been cancelled. If this is unexpected, please contact support.",
            _ => $"eShopOnWeb: An update is available for your order #{reference}."
        };
    }

    private static string Money(decimal amount) => amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
