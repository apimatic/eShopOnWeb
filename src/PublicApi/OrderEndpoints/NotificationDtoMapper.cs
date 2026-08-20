using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(OrderNotification notification)
        => new()
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderSid = notification.ProviderSid,
            Status = notification.DeliveryStatus,
            Body = notification.ContentRedacted ? null : notification.Body,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            ContentRedacted = notification.ContentRedacted,
            CreatedAt = notification.CreatedAt,
            ScheduledSendAt = notification.ScheduledSendAt,
            ResentFromNotificationId = notification.ResentFromNotificationId
        };

    public static System.Collections.Generic.List<NotificationDto> ToDtos(
        System.Collections.Generic.IEnumerable<OrderNotification> notifications)
        => notifications.Select(ToDto).ToList();
}
