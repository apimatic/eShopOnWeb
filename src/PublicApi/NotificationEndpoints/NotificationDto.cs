using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static NotificationDto From(OrderNotification notification)
        => new()
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            ProviderSid = notification.ProviderSid,
            Status = notification.ProviderStatus,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage
        };
}
