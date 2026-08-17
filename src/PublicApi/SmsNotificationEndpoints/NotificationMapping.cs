using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>Maps notification entities to their API shapes. The destination number is deliberately never exposed.</summary>
public static class NotificationMapping
{
    public static NotificationStatusDto ToStatusDto(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        Status = n.DeliveryStatus,
        Delivered = NotificationDeliveryState.IsDelivered(n.DeliveryStatus),
        IsScheduled = n.IsScheduled,
        ScheduledForUtc = n.ScheduledForUtc
    };

    public static OrderNotificationDto ToDetailDto(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        Status = n.DeliveryStatus,
        Delivered = NotificationDeliveryState.IsDelivered(n.DeliveryStatus),
        IsScheduled = n.IsScheduled,
        ScheduledForUtc = n.ScheduledForUtc,
        ProviderMessageSid = n.ProviderMessageSid,
        MessageBody = n.MessageBody,
        ContentRedacted = n.ContentRedacted,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        CreatedAt = n.CreatedAt
    };
}
