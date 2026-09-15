using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationMapper
{
    public static OrderNotificationDto ToDto(OrderNotificationView notification) =>
        new()
        {
            NotificationId = notification.NotificationId,
            OrderId = notification.OrderId,
            Purpose = notification.Purpose.ToString(),
            Body = notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt,
            SendFailureReason = notification.SendFailureReason
        };
}
