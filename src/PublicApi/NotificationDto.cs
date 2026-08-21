using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

using System;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            ScheduledAt = notification.ScheduledAt,
            CreatedAt = notification.CreatedAt,
            ResendOfNotificationId = notification.ResendOfNotificationId
        };
    }
}
