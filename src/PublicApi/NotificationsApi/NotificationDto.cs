using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationsApi;

/// <summary>
/// The shopper/operator view of a single notification. Carries the provider's own
/// identifier and current delivery outcome — <c>notificationId</c> is what the operator
/// endpoints act on.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Type,
    string DeliveryStatus,
    string? ProviderMessageSid,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsScheduled,
    string? ScheduledSendAt,
    bool ContentRedacted,
    int? ResendOfNotificationId,
    string CreatedDate)
{
    public static NotificationDto From(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Type.ToString(),
        n.DeliveryStatus,
        n.ProviderMessageSid,
        n.ErrorCode,
        n.ErrorMessage,
        n.IsScheduled,
        n.ScheduledSendAt?.ToString("o"),
        n.ContentRedacted,
        n.ResendOfNotificationId,
        n.CreatedDate.ToString("o"));
}
