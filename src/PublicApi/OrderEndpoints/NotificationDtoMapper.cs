using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderDateSent = notification.ProviderDateSent,
            CreatedAt = notification.CreatedAt,
            SourceNotificationId = notification.SourceNotificationId
        };
    }
}
