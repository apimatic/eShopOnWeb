using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

internal static class NotificationDtoMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        Body = notification.ContentRedacted || string.IsNullOrEmpty(notification.Body) ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted || string.IsNullOrEmpty(notification.Body),
        ScheduledSendAt = notification.ScheduledSendAt,
        CreatedAt = notification.CreatedAt
    };
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public System.DateTimeOffset? ScheduledSendAt { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
}
