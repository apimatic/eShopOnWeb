using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationReconciliationService : INotificationReconciliationService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsService _smsService;

    public NotificationReconciliationService(IRepository<OrderNotification> notificationRepository,
        ISmsService smsService)
    {
        _notificationRepository = notificationRepository;
        _smsService = smsService;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The provider is asked for this application's own sending number's messages only.
        var providerMessages = await _smsService.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.ProviderMessageSid, out var local))
            {
                var statusMismatch = message.Status != null && local.Status != message.Status;
                report.Matched.Add(new ReconciliationEntry
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = message.ProviderMessageSid,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    StatusMismatch = statusMismatch,
                    DateSent = message.DateSent
                });

                // Bring eShop's belief in line with the provider's authoritative record.
                if (statusMismatch && message.Status != null)
                {
                    local.UpdateProviderStatus(message.Status, message.ErrorCode, message.ErrorMessage);
                    await _notificationRepository.UpdateAsync(local, cancellationToken);
                }
            }
            else
            {
                report.OnlyInProvider.Add(message);
            }
        }

        var providerSids = providerMessages.Select(m => m.ProviderMessageSid).ToHashSet();
        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid == null || !providerSids.Contains(local.ProviderMessageSid))
            {
                report.OnlyInEShop.Add(new ReconciliationLocalEntry
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = local.ProviderMessageSid,
                    LocalStatus = local.Status,
                    CreatedAt = local.CreatedAt
                });
            }
        }

        return report;
    }
}
