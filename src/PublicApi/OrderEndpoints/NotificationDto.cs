using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The provider's identifier for the message.</summary>
    public string? ProviderMessageSid { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification) => new NotificationDto
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        Status = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ProviderMessageSid = notification.ProviderMessageSid,
        ScheduledFor = notification.ScheduledFor,
        ContentRedacted = notification.ContentRedacted,
        Body = notification.ContentRedacted ? null : notification.Body,
        ResendOfNotificationId = notification.ResendOfNotificationId,
        CreatedAt = notification.CreatedAt
    };
}
