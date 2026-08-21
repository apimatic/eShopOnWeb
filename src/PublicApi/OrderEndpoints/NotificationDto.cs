using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SendAt { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentDisposed || string.IsNullOrEmpty(notification.Body) ? null : notification.Body,
            ContentDisposed = notification.ContentDisposed,
            ProviderMessageSid = notification.ProviderMessageSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            CreatedAt = notification.CreatedAt,
            SendAt = notification.SendAt,
            ResendOfNotificationId = notification.ResendOfNotificationId
        };
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public List<NotificationDto> Notifications { get; set; } = new();
}
