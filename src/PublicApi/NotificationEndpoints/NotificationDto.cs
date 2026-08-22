using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public System.DateTimeOffset? ScheduledFor { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
    public int? SourceNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ProviderErrorCode,
        ErrorMessage = notification.ProviderErrorMessage ?? notification.LocalFailureReason,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        SourceNotificationId = notification.SourceNotificationId
    };
}
