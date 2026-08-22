using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? SourceNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Body = notification.ContentRedacted || string.IsNullOrEmpty(notification.Body) ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted || string.IsNullOrEmpty(notification.Body),
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ScheduledSendAt = notification.ScheduledSendAt,
        CreatedAt = notification.CreatedAt,
        SourceNotificationId = notification.SourceNotificationId
    };
}
