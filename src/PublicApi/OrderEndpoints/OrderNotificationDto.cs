using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The message text; null once its content has been disposed of.</summary>
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Type = notification.Type.ToString(),
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ProviderMessageSid = notification.ProviderMessageSid,
        CreatedAt = notification.CreatedAt,
        SentAt = notification.SentAt,
        ScheduledFor = notification.ScheduledFor
    };
}
