using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public record NotificationDto(
    int NotificationId,
    string Type,
    string? Content,
    bool ContentDisposed,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? LastSynchronizedAt)
{
    public static NotificationDto FromEntity(OrderNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.Content,
        notification.ContentDisposedAt.HasValue,
        notification.ProviderMessageSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderDateSent,
        notification.LastSynchronizedAt);
}
