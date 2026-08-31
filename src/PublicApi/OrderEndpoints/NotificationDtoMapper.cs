using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationDtoMapper
{
    public static NotificationDto ToDto(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Type = notification.Type.ToString(),
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        LastUpdatedAt = notification.LastUpdatedAt,
        ContentRedacted = notification.ContentRedacted,
        Body = notification.ContentRedacted ? null : notification.Body
    };
}
