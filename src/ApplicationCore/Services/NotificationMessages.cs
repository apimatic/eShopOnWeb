using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the SMS text for each point in an order's life. Messages carry only the order number — never
/// any shopper contact detail.
/// </summary>
public static class NotificationMessages
{
    public static string For(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationKind.OrderDispatched =>
            $"eShop: good news — your order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp =>
            $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationKind.OrderCanceled =>
            $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact support.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind.")
    };
}
