using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed record NotificationDto(
    int NotificationId,
    string Kind,
    string ProviderStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? Content,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? ScheduledFor,
    int? ResendOfNotificationId)
{
    public static NotificationDto FromEntity(OrderNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.Content,
        notification.ContentDisposedAt.HasValue,
        notification.CreatedAt,
        notification.SentAt,
        notification.ScheduledFor,
        notification.ResendOfNotificationId);
}
