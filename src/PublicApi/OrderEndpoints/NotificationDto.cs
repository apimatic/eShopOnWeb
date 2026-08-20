using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool BodyRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.BodyRedacted ? null : notification.Body,
            BodyRedacted = notification.BodyRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ScheduledSendAt = notification.ScheduledSendAt,
            ProviderDateSent = notification.ProviderDateSent,
            CreatedAt = notification.CreatedAt
        };
    }
}
