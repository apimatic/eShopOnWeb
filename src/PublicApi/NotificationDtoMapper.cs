using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

internal static class NotificationDtoMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ProviderMessageSid = notification.ProviderMessageSid ?? string.Empty,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            ScheduledSendAt = notification.ScheduledSendAt,
            ContentRedacted = notification.ContentRedacted,
            SourceNotificationId = notification.SourceNotificationId
        };
    }

    public static List<OrderNotificationDto> ToDtos(IEnumerable<OrderNotification> notifications)
        => notifications.Select(ToDto).ToList();
}
