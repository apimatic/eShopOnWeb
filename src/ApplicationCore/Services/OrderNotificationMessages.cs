using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Composes the SMS text for each kind of order notification. Bodies are deterministic in the
/// notification type and order id, so a resend can reconstruct the original text without the
/// application having to store message content. Kept short and GSM-7 to stay single-segment.
/// </summary>
public static class OrderNotificationMessages
{
    public static string Compose(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!",
        NotificationType.OrderDispatched => $"eShop: good news - your order #{orderId} is on its way!",
        NotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We would love your feedback.",
        NotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. If this is unexpected, please contact us.",
        _ => $"eShop: an update about your order #{orderId}."
    };
}
