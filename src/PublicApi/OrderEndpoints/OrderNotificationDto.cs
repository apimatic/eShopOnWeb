using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification) =>
        new()
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentDisposed ? null : notification.Body,
            ContentDisposed = notification.ContentDisposed,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ScheduledSendAt = notification.ScheduledSendAt,
            CreatedAt = notification.CreatedAt
        };
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public System.DateTimeOffset? ScheduledSendAt { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
}
