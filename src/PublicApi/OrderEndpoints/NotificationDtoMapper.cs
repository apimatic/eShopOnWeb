using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind,
        Status = notification.ProviderStatus,
        ProviderMessageSid = notification.ProviderMessageSid,
        ErrorCode = notification.ProviderErrorCode,
        ErrorMessage = notification.ProviderErrorMessage,
        Body = notification.ContentRedacted ? string.Empty : notification.Body,
        ScheduledSendAt = notification.ScheduledSendAt,
        ContentRedacted = notification.ContentRedacted
    };
}
