using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        Status = notification.ProviderStatus,
        ProviderMessageSid = notification.ProviderMessageSid,
        ErrorCode = notification.ProviderErrorCode,
        ErrorMessage = notification.ProviderErrorMessage,
        Body = notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}
