using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Composes the short SMS text for each order-lifecycle event.</summary>
public static class NotificationMessages
{
    public static string For(NotificationKind kind, Order order)
    {
        var total = order.Total().ToString("0.00", CultureInfo.InvariantCulture);
        return kind switch
        {
            NotificationKind.OrderPlaced =>
                $"eShop: your order #{order.Id} has been placed. Total {total}. Thanks for shopping with us!",
            NotificationKind.OrderDispatched =>
                $"eShop: good news - your order #{order.Id} is on its way!",
            NotificationKind.OrderCancelled =>
                $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
            NotificationKind.DeliveryFollowUp =>
                $"eShop: how did the delivery of your order #{order.Id} go? We would love your feedback.",
            _ => $"eShop: an update on your order #{order.Id}."
        };
    }
}
