using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public static class NotificationMapper
{
    public static NotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Body = notification.ContentDisposed ? null : notification.Body,
        ContentDisposed = notification.ContentDisposed,
        ProviderSid = notification.ProviderMessageSid,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        SendAt = notification.SendAt,
        CreatedAt = notification.CreatedAt,
        ResentFromNotificationId = notification.ResentFromNotificationId,
        LocalFailure = notification.LocalFailure
    };
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public System.DateTimeOffset? SendAt { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
    public int? ResentFromNotificationId { get; set; }
    public string? LocalFailure { get; set; }
}
