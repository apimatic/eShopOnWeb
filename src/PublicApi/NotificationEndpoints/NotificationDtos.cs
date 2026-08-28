using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed record NotificationDto(
    int NotificationId,
    string Kind,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt,
    DateTimeOffset? ContentDisposedAt,
    int? OriginalNotificationId)
{
    public static NotificationDto FromEntity(OrderNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.Body,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderSentAt,
        notification.ContentDisposedAt,
        notification.OriginalNotificationId);
}
