using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? ResentFromNotificationId { get; set; }

    public static NotificationDto FromEntity(Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate.OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Status = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt,
            ResentFromNotificationId = notification.ResentFromNotificationId
        };
    }
}
