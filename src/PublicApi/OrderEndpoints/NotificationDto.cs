using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public string? ErrorMessage { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Type = notification.Type.ToString(),
            Status = notification.Status,
            ProviderMessageSid = notification.ProviderMessageSid,
            Body = notification.ContentDisposed ? null : notification.Body,
            ContentDisposed = notification.ContentDisposed,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            ErrorMessage = notification.ErrorMessage
        };
    }
}
