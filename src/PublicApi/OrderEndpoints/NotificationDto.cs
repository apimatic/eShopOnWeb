using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool BodyRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto FromEntity(Notification notification) => new()
    {
        NotificationId = notification.Id,
        MessageSid = notification.MessageSid,
        Type = notification.Type.ToString(),
        Status = notification.Status,
        Body = notification.BodyRedacted ? null : notification.Body,
        BodyRedacted = notification.BodyRedacted,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}
