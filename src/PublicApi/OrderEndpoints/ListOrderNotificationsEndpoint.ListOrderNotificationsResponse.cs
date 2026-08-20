using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ResentFromNotificationId { get; set; }

    public static OrderNotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Type = notification.Type.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.ContentRedacted ? null : notification.Body,
        CreatedAt = notification.CreatedAt,
        ProviderDateSent = notification.ProviderDateSent,
        ScheduledAt = notification.ScheduledAt,
        ContentRedacted = notification.ContentRedacted,
        ResentFromNotificationId = notification.ResentFromNotificationId
    };
}
