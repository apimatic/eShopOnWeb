using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        NotificationType = notification.NotificationType.ToString(),
        MessageSid = notification.MessageSid,
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor,
        ContentRedacted = notification.ContentRedacted,
        Body = notification.ContentRedacted ? null : notification.Body
    };
}
