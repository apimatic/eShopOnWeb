using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderMessageSid { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification) => new NotificationDto
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        To = notification.ToNumber,
        Body = notification.ContentRedacted ? null : notification.Body,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ProviderErrorCode,
        ErrorMessage = notification.ProviderErrorMessage,
        ProviderMessageSid = notification.ProviderMessageSid,
        ScheduledForUtc = notification.ScheduledForUtc,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt
    };
}
