using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

internal static class NotificationMapper
{
    public static NotificationDto ToDto(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        State = n.State.ToString(),
        Recipient = n.Recipient,
        Body = n.Body,
        ContentRedacted = n.ContentRedacted,
        ProviderSid = n.ProviderSid,
        ProviderStatus = n.ProviderStatus,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ProviderDateSent = n.ProviderDateSent,
        CreatedAt = n.CreatedAt
    };
}
