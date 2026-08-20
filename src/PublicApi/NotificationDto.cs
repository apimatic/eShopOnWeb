using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public int? OriginalNotificationId { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Type = notification.Type.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            Body = notification.ContentRedacted ? string.Empty : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            DateSent = notification.DateSent,
            ScheduledAt = notification.ScheduledAt,
            OriginalNotificationId = notification.OriginalNotificationId
        };
    }
}
