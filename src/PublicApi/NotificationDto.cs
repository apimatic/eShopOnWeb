using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public int? SourceNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Body = notification.ContentRedacted ? null : notification.Body,
        ProviderMessageSid = notification.ProviderMessageSid,
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        CreatedAt = notification.CreatedAt,
        DateSent = notification.DateSent,
        ScheduledSendAt = notification.ScheduledSendAt,
        ContentRedacted = notification.ContentRedacted,
        SourceNotificationId = notification.SourceNotificationId
    };
}
