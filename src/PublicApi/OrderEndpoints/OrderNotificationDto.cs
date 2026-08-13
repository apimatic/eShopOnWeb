using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// How a single notification reached (or failed to reach) the shopper. The destination number is
/// deliberately not exposed here.
/// </summary>
public record OrderNotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Status,
    string? ProviderMessageSid,
    bool IsScheduled,
    DateTimeOffset? ScheduledSendAt,
    bool ContentDisposed,
    DateTimeOffset CreatedAt)
{
    public static OrderNotificationDto FromEntity(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        n.DeliveryStatus,
        n.ProviderMessageSid,
        n.IsScheduled,
        n.ScheduledSendAt,
        n.ContentDisposed,
        n.CreatedAt);
}
