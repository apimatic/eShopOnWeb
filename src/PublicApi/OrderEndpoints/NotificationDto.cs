using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationDto
{
    public static object From(OrderNotification notification)
    {
        return new
        {
            notificationId = notification.Id,
            orderId = notification.OrderId,
            kind = notification.Kind.ToString(),
            status = notification.ProviderStatus,
            providerSid = notification.ProviderSid,
            body = notification.BodyRedacted ? null : notification.Body,
            bodyRedacted = notification.BodyRedacted,
            errorCode = notification.ErrorCode,
            errorMessage = notification.ErrorMessage,
            sendAt = notification.SendAt,
            createdAt = notification.CreatedAt
        };
    }
}
