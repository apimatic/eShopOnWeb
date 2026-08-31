using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationDtoMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        NotificationType = notification.NotificationType.ToString(),
        ProviderMessageId = notification.ProviderMessageId,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        ScheduledFor = notification.ScheduledFor,
        ContentRedacted = notification.ContentRedacted,
        Body = notification.ContentRedacted ? null : notification.Body,
        ResendOfId = notification.ResendOfId,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}
