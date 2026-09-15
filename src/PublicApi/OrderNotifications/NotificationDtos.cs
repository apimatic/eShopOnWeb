using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.Twilio;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    bool ContentRedacted,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    int? ResendOfNotificationId)
{
    public static NotificationDto FromEntity(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.Body,
        notification.ContentRedacted,
        notification.ProviderMessageSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ResendOfNotificationId);
}

public sealed record MyOrderDto(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record ReconciliationEntry(
    int? NotificationId,
    string? ProviderMessageSid,
    string Match,
    string? LocalStatus,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ProviderDate,
    string? ProviderFrom,
    string? ProviderTo);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries)
{
    public static ReconciliationResponse Build(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyCollection<OrderNotification> local,
        IReadOnlyCollection<TwilioMessage> provider)
    {
        var localBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        foreach (var notification in local)
        {
            if (notification.ProviderMessageSid != null)
            {
                localBySid[notification.ProviderMessageSid] = notification;
            }
        }

        var providerBySid = new Dictionary<string, TwilioMessage>(StringComparer.Ordinal);
        foreach (var message in provider)
        {
            providerBySid[message.Sid] = message;
        }

        var entries = new List<ReconciliationEntry>();
        foreach (var message in provider.OrderBy(x => x.DateSent ?? x.DateCreated))
        {
            localBySid.TryGetValue(message.Sid, out var notification);
            entries.Add(new ReconciliationEntry(
                notification?.Id,
                message.Sid,
                notification == null ? "providerOnly" : "matched",
                notification?.ProviderStatus,
                message.Status,
                message.ErrorCode,
                message.DateSent ?? message.DateCreated,
                message.From,
                message.To));
        }

        foreach (var notification in local.Where(x => x.ProviderMessageSid == null || !providerBySid.ContainsKey(x.ProviderMessageSid)))
        {
            entries.Add(new ReconciliationEntry(
                notification.Id,
                notification.ProviderMessageSid,
                "localOnly",
                notification.ProviderStatus,
                null,
                notification.ProviderErrorCode,
                null,
                null,
                null));
        }

        return new ReconciliationResponse(from, to, entries);
    }
}
