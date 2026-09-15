using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// How one order notification is presented over the API. Carries the provider's identifier and the
/// current delivery outcome so an operator endpoint can act on it. <see cref="Body"/> is null once the
/// content has been disposed of.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Type,
    string Status,
    int? ErrorCode,
    string? ProviderMessageSid,
    string? To,
    string? Body,
    bool ContentRedacted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? ScheduledSendAt,
    int? ResendOfNotificationId)
{
    public static NotificationDto From(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Type.ToString(),
        n.Status,
        n.ErrorCode,
        n.ProviderMessageSid,
        n.ToNumber,
        n.Body,
        n.ContentRedacted,
        n.CreatedAt,
        n.SentAt,
        n.ScheduledSendAt,
        n.ResendOfNotificationId);
}
