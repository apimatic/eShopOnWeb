using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class OrderNotificationMessages
{
    public static string For(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed.",
        OrderNotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go?",
        OrderNotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind.")
    };
}
