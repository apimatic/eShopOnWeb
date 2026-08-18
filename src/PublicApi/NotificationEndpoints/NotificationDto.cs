using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent for an order and what became of it. Carries the provider's identifier and current
/// delivery outcome (the operator endpoints act on <see cref="NotificationId"/>). It deliberately does
/// not expose the destination number.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string DeliveryStatus,
    string? ProviderMessageSid,
    int? ErrorCode,
    string? ErrorMessage,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt)
{
    public static NotificationDto From(Notification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.DeliveryStatus,
        notification.ProviderMessageSid,
        notification.ErrorCode,
        notification.ErrorMessage,
        notification.IsScheduled,
        notification.ScheduledFor,
        notification.ContentDisposed,
        notification.CreatedAt,
        notification.SentAt);
}
