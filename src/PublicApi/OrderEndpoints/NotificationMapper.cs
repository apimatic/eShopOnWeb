using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationMapper
{
    public static NotificationDto ToDto(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            NotificationType = notification.NotificationType.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            AcceptedByProvider = notification.AcceptedByProvider,
            Body = notification.Body,
            ContentDisposed = notification.ContentDisposed,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt
        };
    }
}
