using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public string? Body { get; set; }
    public bool IsContentRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        NotificationType = notification.NotificationType.ToString(),
        Status = notification.Status,
        MessageSid = notification.MessageSid,
        Body = notification.IsContentRedacted ? null : notification.Body,
        IsContentRedacted = notification.IsContentRedacted,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        CreatedUtc = notification.CreatedUtc,
        ScheduledForUtc = notification.ScheduledForUtc
    };
}
