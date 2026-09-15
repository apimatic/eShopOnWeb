using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the text of each order notification. Message bodies are derived from the notification kind and
/// the order id rather than stored, so there is no shopper-facing content held by this application to
/// dispose of — content disposal is a provider-side concern only.
/// </summary>
public static class NotificationMessageBuilder
{
    public static string Build(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"eShop: thanks! Your order #{orderId} has been placed.",
        NotificationKind.OrderDispatched =>
            $"eShop: good news — your order #{orderId} is on its way!",
        NotificationKind.OrderCancelled =>
            $"eShop: your order #{orderId} has been cancelled.",
        NotificationKind.DeliveryFollowUp =>
            $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind.")
    };
}
