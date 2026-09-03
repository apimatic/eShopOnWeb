using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; }
    public string Status { get; set; }
    public string? ProviderSid { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }

    public static OrderNotificationDto From(ApplicationCore.Entities.NotificationAggregate.OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Status = notification.ProviderStatus,
            ProviderSid = notification.ProviderSid,
            Body = notification.BodyForDisplay,
            ContentRedacted = notification.ContentRedacted,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt
        };
    }
}
