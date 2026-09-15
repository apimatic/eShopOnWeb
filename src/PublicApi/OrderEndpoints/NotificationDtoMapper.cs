using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(this Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        Status = n.ProviderStatus,
        ErrorCode = n.ProviderErrorCode,
        CreatedAt = n.CreatedAt,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed
    };
}
