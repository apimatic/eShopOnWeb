using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationsFeature;

/// <summary>
/// A notification as reported to callers. Carries its own <c>notificationId</c> (what the
/// operator endpoints act on) and the state the provider owns — its identifier and current
/// delivery outcome. The destination number is masked; its full form and the body are never
/// exposed here.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string DeliveryStatus,
    int? ErrorCode,
    string? ProviderMessageSid,
    string ToMasked,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ScheduledSendAt,
    int? ResendOfNotificationId)
{
    public static NotificationDto From(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        n.ProviderStatus,
        n.ProviderErrorCode,
        n.ProviderMessageSid,
        PhoneMask.Mask(n.ToPhoneNumber),
        n.ContentDisposed,
        n.CreatedAt,
        n.SubmittedAt,
        n.ScheduledSendAt,
        n.ResendOfNotificationId);
}
