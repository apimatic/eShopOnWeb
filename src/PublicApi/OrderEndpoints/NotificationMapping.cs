using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class NotificationMapping
{
    public static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Status = notification.Status,
            ToNumber = notification.ToNumber,
            Body = notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            ScheduledFor = notification.ScheduledFor,
            CreatedOn = notification.CreatedOn,
            LastUpdatedOn = notification.LastUpdatedOn
        };
    }
}
