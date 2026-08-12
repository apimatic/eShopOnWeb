using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// A notification as reported to callers — its identifier (what the operator endpoints act on) and its
/// provider-owned state. Deliberately omits the destination number and the message body.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset? ScheduledSendAt,
    bool ContentDisposed,
    int? ResendOfNotificationId,
    DateTimeOffset CreatedAt)
{
    public static NotificationDto From(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        n.ProviderStatus,
        n.ProviderMessageSid,
        n.ProviderErrorCode,
        n.ScheduledSendAt,
        n.ContentDisposed,
        n.ResendOfNotificationId,
        n.CreatedAt);
}
