using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Default <see cref="INotificationReconciliationService"/>. Asks the provider for the messages it
/// recorded as sent from this application's configured sending number over the range, and lines them
/// up against the notifications eShop believes it sent in that range, keyed by provider message id.
/// </summary>
public class NotificationReconciliationService : INotificationReconciliationService
{
    private readonly ISmsProvider _smsProvider;
    private readonly IReadRepository<Notification> _notificationRepository;

    public NotificationReconciliationService(
        ISmsProvider smsProvider,
        IReadRepository<Notification> notificationRepository)
    {
        _smsProvider = smsProvider;
        _notificationRepository = notificationRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider is asked for its own configured-sender messages in the range — the sender filter
        // is applied by the provider, not by us after a wider answer.
        var providerMessages = await _smsProvider.ListOutboundFromConfiguredSenderAsync(from, to, cancellationToken);

        var localNotifications = await _notificationRepository.ListAsync(new NotificationsSentInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var atProviderOnly = new List<ReconciliationEntry>();
        var atEShopOnly = new List<ReconciliationEntry>();

        foreach (var providerMessage in providerBySid.Values)
        {
            if (localBySid.TryGetValue(providerMessage.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = providerMessage.Sid,
                    ProviderStatus = providerMessage.Status,
                    EShopStatus = local.ProviderStatus,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    DateSent = providerMessage.DateSent
                });
            }
            else
            {
                atProviderOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = providerMessage.Sid,
                    ProviderStatus = providerMessage.Status,
                    EShopStatus = null,
                    DateSent = providerMessage.DateSent
                });
            }
        }

        foreach (var local in localBySid.Values)
        {
            if (!providerBySid.ContainsKey(local.ProviderMessageSid!))
            {
                atEShopOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    ProviderStatus = null,
                    EShopStatus = local.ProviderStatus,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    DateSent = null
                });
            }
        }

        return new ReconciliationReport(from, to, matched, atProviderOnly, atEShopOnly);
    }
}
