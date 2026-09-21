using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the message text for each notification type deterministically from the type and order id.
/// Kept out of the stored notification so that disposing of the provider's copy leaves no second
/// copy behind, and so a resend can reproduce the same text without retaining content.
/// </summary>
public static class NotificationMessageBuilder
{
    public static string Build(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced =>
            $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationType.OrderDispatched =>
            $"eShop: good news - your order #{orderId} is on its way.",
        NotificationType.DeliveryFollowUp =>
            $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
        NotificationType.OrderCancelled =>
            $"eShop: your order #{orderId} has been cancelled. Please contact us with any questions.",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown notification type")
    };
}
