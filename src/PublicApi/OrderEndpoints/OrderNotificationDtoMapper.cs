using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderNotificationDtoMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            Body = notification.ContentRedacted ? null : notification.Body
        };
    }
}
