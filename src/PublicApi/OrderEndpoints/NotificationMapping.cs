using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Maps an <see cref="OrderNotification"/> to its API view. The destination number is never
/// included — only the identifier and the provider-owned delivery state.</summary>
public static class NotificationMapping
{
    public static NotificationView ToView(this OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        DeliveryState = n.DeliveryState.ToString(),
        ProviderStatus = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ProviderDateSent = n.ProviderDateSent,
        IsScheduledFollowUp = n.IsScheduledFollowUp,
        ContentDisposed = n.ContentDisposed,
        CreatedDate = n.CreatedDate
    };
}
