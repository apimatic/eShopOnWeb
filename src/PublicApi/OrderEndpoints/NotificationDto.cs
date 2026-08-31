using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? MessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        To = notification.ToNumber,
        Body = notification.Body,
        MessageSid = notification.MessageSid,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor,
        ContentRedacted = notification.ContentRedacted
    };
}
