using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class NotificationMapper
{
    public static NotificationView ToView(OrderNotification notification)
    {
        return new NotificationView
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderStatus = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ScheduledSendAt = notification.ScheduledSendAt,
            CreatedAt = notification.CreatedAt,
            SourceNotificationId = notification.SourceNotificationId
        };
    }

    public static string BodyFor(OrderNotificationKind kind, int orderId)
    {
        return kind switch
        {
            OrderNotificationKind.OrderPlaced =>
                $"Your eShopOnWeb order #{orderId} has been placed. Thank you for shopping with us.",
            OrderNotificationKind.OrderDispatched =>
                $"Your eShopOnWeb order #{orderId} is on its way.",
            OrderNotificationKind.DeliveryFollowUp =>
                $"How did the delivery of your eShopOnWeb order #{orderId} go? We would love to hear your feedback.",
            OrderNotificationKind.OrderCancelled =>
                $"Your eShopOnWeb order #{orderId} has been cancelled.",
            _ => $"An update on your eShopOnWeb order #{orderId}."
        };
    }
}
