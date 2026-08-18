using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class NotificationMapper
{
    public static NotificationDto ToDto(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.SendFailed ? "not_sent" : (n.ProviderStatus ?? "unknown"),
        ProviderSid = n.ProviderSid,
        ProviderErrorCode = n.ProviderErrorCode,
        SendFailed = n.SendFailed,
        SendFailureReason = n.SendFailureReason,
        ContentRedacted = n.ContentRedacted,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedDate = n.CreatedDate
    };
}
