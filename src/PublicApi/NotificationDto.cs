using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public int? ResentFromNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            DeliveryStatus = notification.DeliveryStatus,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            Body = notification.GetBodyForDisplay(),
            ContentRedacted = notification.ContentRedacted,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt,
            LastSyncedAt = notification.LastSyncedAt,
            ResentFromNotificationId = notification.ResentFromNotificationId
        };
    }
}
