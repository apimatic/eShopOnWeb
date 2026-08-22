using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

internal static class NotificationMapping
{
    public static OrderNotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        ProviderSid = notification.ProviderSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        Body = notification.ContentRedacted ? null : notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        ProviderDateSent = notification.ProviderDateSent
    };
}
