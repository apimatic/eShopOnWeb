using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }

    public static NotificationDto FromEntity(Notification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Type = notification.Type.ToString(),
            Status = notification.Status,
            MessageSid = notification.MessageSid,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            ContentRedacted = notification.ContentRedacted,
            Body = notification.Body
        };
    }
}
