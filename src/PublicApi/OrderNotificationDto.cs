using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static OrderNotificationDto From(OrderNotification notification) =>
        new()
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            DeliveryStatus = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            Body = notification.BodyForDisplay,
            ContentRedacted = notification.ContentRedacted,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt
        };
}
