using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? SourceNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate.OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Status = notification.ProviderStatus,
            ProviderSid = notification.ProviderMessageSid,
            ErrorCode = notification.ProviderErrorCode,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ScheduledSendAt = notification.ScheduledSendAt,
            SourceNotificationId = notification.SourceNotificationId,
            CreatedAt = notification.CreatedAt
        };
    }
}
