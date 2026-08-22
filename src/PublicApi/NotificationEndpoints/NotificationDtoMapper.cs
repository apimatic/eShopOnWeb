using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public static class NotificationDtoMapper
{
    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind,
            ProviderMessageSid = notification.ProviderMessageSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ErrorCode,
            ContentRedacted = notification.ContentRedacted,
            Body = notification.ContentRedacted ? null : notification.Body,
            ScheduledSendAt = notification.ScheduledSendAt,
            CreatedAt = notification.CreatedAt
        };
    }
}
