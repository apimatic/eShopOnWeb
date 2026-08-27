using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        Status = notification.Status,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        ScheduledForUtc = notification.ScheduledForUtc,
        CreatedUtc = notification.CreatedUtc
    };
}
