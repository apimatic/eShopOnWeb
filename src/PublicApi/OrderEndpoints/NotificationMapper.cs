using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationMapper
{
    public static NotificationDto? ToDto(OrderNotification? notification)
    {
        if (notification is null)
        {
            return null;
        }

        return new NotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind,
            ProviderSid = notification.ProviderSid,
            Status = notification.ProviderStatus,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledAt = notification.ScheduledAt,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage
        };
    }
}
