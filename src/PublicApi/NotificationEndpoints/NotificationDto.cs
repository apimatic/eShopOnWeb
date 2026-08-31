using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string To { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        Status = notification.ProviderStatus ?? "pending",
        ErrorCode = notification.ProviderErrorCode,
        ErrorMessage = notification.ProviderErrorMessage,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        To = notification.ToNumber,
        MessageSid = notification.MessageSid,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor,
        SentAt = notification.SentAt
    };
}
