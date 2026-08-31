using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationMapping
{
    public static OrderNotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Type = notification.Type.ToString(),
        Body = notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        SendFailed = notification.SendFailed,
        CreatedAt = notification.CreatedAt,
        ScheduledFor = notification.ScheduledFor
    };
}
