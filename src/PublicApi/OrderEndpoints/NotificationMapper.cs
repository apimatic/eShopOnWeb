using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationMapper
{
    public static NotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        ToNumber = notification.ToNumber,
        Body = notification.Body,
        MessageSid = notification.MessageSid,
        ScheduledFor = notification.ScheduledFor,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}
