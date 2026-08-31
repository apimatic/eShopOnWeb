using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationMapping
{
    public static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Body = notification.Body,
            BodyRedacted = notification.BodyRedacted,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt,
            LastUpdatedAt = notification.LastUpdatedAt,
            ResendOfId = notification.ResendOfId
        };
    }
}
