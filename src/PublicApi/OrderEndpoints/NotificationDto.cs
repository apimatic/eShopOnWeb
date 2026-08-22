using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationMapper
{
    public static NotificationDto ToDto(OrderNotification notification)
        => new()
        {
            NotificationId = notification.Id,
            Kind = notification.Kind,
            ProviderSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ErrorCode = notification.ErrorCode,
            Body = notification.ContentDisposed ? string.Empty : notification.Body,
            ContentDisposed = notification.ContentDisposed,
            ScheduledAt = notification.ScheduledAt,
            CreatedAt = notification.CreatedAt,
            ResentFromNotificationId = notification.ResentFromNotificationId
        };
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool ContentDisposed { get; set; }
    public System.DateTimeOffset? ScheduledAt { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
    public int? ResentFromNotificationId { get; set; }
}
