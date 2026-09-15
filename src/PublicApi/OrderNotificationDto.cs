using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

namespace Microsoft.eShopWeb.PublicApi;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? SendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ResentFromNotificationId { get; set; }

    public static OrderNotificationDto From(OrderNotification notification)
        => new()
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Status = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderErrorCode = notification.ProviderErrorCode,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            SendAt = notification.SendAt,
            CreatedAt = notification.CreatedAt,
            ResentFromNotificationId = notification.ResentFromNotificationId
        };
}
