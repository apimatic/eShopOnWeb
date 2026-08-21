using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }
    public int? SourceNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification)
        => new()
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderStatus = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            ScheduledSendAt = notification.ScheduledSendAt,
            CreatedAt = notification.CreatedAt,
            ContentRedacted = notification.ContentRedacted,
            Body = notification.ContentRedacted ? null : notification.Body,
            SourceNotificationId = notification.SourceNotificationId
        };
}
