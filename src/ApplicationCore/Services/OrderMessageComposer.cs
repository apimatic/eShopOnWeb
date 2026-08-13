using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the SMS text a shopper receives at each point in an order's life. Kept in one place so the
/// wording is consistent across the initial send, a re-send, and a re-derivation after content disposal.
/// </summary>
public static class OrderMessageComposer
{
    public static string Compose(Order order, NotificationType type)
    {
        var total = order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"));
        return type switch
        {
            NotificationType.OrderPlaced =>
                $"eShopOnWeb: Thanks! Your order #{order.Id} for {total} has been placed.",
            NotificationType.OrderDispatched =>
                $"eShopOnWeb: Good news - your order #{order.Id} is on its way!",
            NotificationType.DeliveryFollowUp =>
                $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love your feedback.",
            NotificationType.OrderCancelled =>
                $"eShopOnWeb: Your order #{order.Id} has been cancelled. If this was unexpected, please get in touch.",
            _ => $"eShopOnWeb: An update about your order #{order.Id}."
        };
    }
}
