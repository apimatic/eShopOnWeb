using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A notification as returned by the API. Carries the message's own <c>notificationId</c> (what the operator
/// endpoints act on) and the state the provider owns — its SID and current delivery outcome — but never the
/// destination number and never the message text.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Type,
    string Status,
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? ErrorCode,
    bool ContentDisposed,
    DateTimeOffset? ScheduledSendAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static NotificationDto From(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Type.ToString(),
        n.Status.ToString(),
        n.ProviderMessageSid,
        n.ProviderStatusRaw,
        n.ErrorCode,
        n.ContentDisposed,
        n.ScheduledSendAt,
        n.CreatedAt,
        n.UpdatedAt);
}
