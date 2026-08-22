using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationReconciliationService : INotificationReconciliationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsGateway _smsGateway;

    public NotificationReconciliationService(
        IRepository<OrderNotification> notifications,
        ISmsGateway smsGateway)
    {
        _notifications = notifications;
        _smsGateway = smsGateway;
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentByConfiguredNumberAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(
            new OrderNotificationsInRangeSpecification(from, to),
            cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new List<NotificationReconciliationEntry>();
        var providerOnly = new List<NotificationReconciliationEntry>();
        var localOnly = new List<NotificationReconciliationEntry>();

        foreach (var (sid, local) in localBySid)
        {
            if (providerBySid.TryGetValue(sid, out var provider))
            {
                matched.Add(new NotificationReconciliationEntry
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = sid,
                    LocalStatus = local.ProviderStatus,
                    ProviderStatus = provider.Status,
                    Match = "matched"
                });
            }
            else
            {
                localOnly.Add(new NotificationReconciliationEntry
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = sid,
                    LocalStatus = local.ProviderStatus,
                    Match = "localOnly"
                });
            }
        }

        foreach (var local in localNotifications.Where(n => string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            localOnly.Add(new NotificationReconciliationEntry
            {
                NotificationId = local.Id,
                LocalStatus = local.ProviderStatus,
                Match = "localOnly"
            });
        }

        foreach (var (sid, provider) in providerBySid)
        {
            if (!localBySid.ContainsKey(sid))
            {
                providerOnly.Add(new NotificationReconciliationEntry
                {
                    ProviderMessageSid = sid,
                    ProviderStatus = provider.Status,
                    Match = "providerOnly"
                });
            }
        }

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            ProviderOnly = providerOnly,
            LocalOnly = localOnly
        };
    }
}
