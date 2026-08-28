using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed record NotificationResponse(
    int NotificationId,
    string Kind,
    string? Content,
    bool ContentDisposed,
    string ProviderStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static NotificationResponse From(OrderNotification notification) => new(
        notification.Id,
        notification.Kind.ToString(),
        notification.Content,
        notification.ContentDisposed,
        notification.ProviderStatus,
        notification.ProviderMessageSid,
        notification.ProviderErrorCode,
        notification.ScheduledFor,
        notification.CreatedAt,
        notification.UpdatedAt);
}
