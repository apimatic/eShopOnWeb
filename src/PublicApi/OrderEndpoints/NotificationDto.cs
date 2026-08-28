using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public sealed record NotificationDto(int NotificationId, string Kind, string? Content,
    string? ProviderMessageId, string ProviderStatus, int? ProviderErrorCode,
    DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ContentDisposedAt, int? ResendOfNotificationId)
{
    public static NotificationDto From(OrderNotification notification) => new(
        notification.Id, notification.Kind, notification.Body, notification.ProviderMessageId,
        notification.ProviderStatus, notification.ProviderErrorCode, notification.CreatedAt,
        notification.ScheduledFor, notification.ProviderDateSent,
        notification.ContentDisposedAt, notification.ResendOfNotificationId);
}
