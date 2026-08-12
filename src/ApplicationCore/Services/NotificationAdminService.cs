using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Operator actions over notifications: idempotent resend, content disposal (on the provider's
/// side, not merely locally), and reconciliation against the provider's own record.
/// </summary>
public class NotificationAdminService : INotificationAdminService
{
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<Notification> notificationRepository,
        ISmsProvider smsProvider,
        IAppLogger<NotificationAdminService> logger)
    {
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: if we have already produced a notification under this key, return it and do
        // not send a second message.
        var alreadyDone = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            return ResendResult.Duplicate(alreadyDone.Id);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return ResendResult.NotFound;
        }

        if (original.ContentDisposed || original.Body is null)
        {
            // The message content has been disposed of; there is nothing to re-send.
            return ResendResult.ContentDisposed;
        }

        // Persist the new notification carrying the idempotency key BEFORE sending, so a concurrent
        // repeat under the same key sees it and returns without sending a second message.
        var resent = new Notification(original.OrderId, original.OwnerId, original.ToNumber, original.Body,
            original.Kind, idempotencyKey);
        await _notificationRepository.AddAsync(resent, cancellationToken);

        try
        {
            var sent = await _smsProvider.SendAsync(original.ToNumber, original.Body, cancellationToken);
            resent.MarkSent(sent.ProviderMessageSid, sent.Status, sent.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {0} failed to send: {1}", notificationId, ex.GetType().Name);
            resent.MarkSendFailed(null);
        }

        await _notificationRepository.UpdateAsync(resent, cancellationToken);
        return ResendResult.Sent(resent.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose on the provider's side first so the text is no longer retrievable from the provider
        // either. If this throws, the caller surfaces an error and the content is left intact on both
        // sides rather than hidden only locally.
        if (notification.ProviderMessageSid is not null && !notification.ContentDisposed)
        {
            await _smsProvider.DisposeContentAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for messages sent from the application's own configured sending number
        // within the range (server-side filter) — the account carries other traffic that is not ours.
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, cancellationToken);

        // What eShop believes it actually sent in the range: notifications handed to the provider
        // (a SID exists), created within the range, and not a never-sent scheduled/cancelled follow-up.
        var allNotifications = await _notificationRepository.ListAsync(cancellationToken);
        var eShopSent = allNotifications
            .Where(n => n.ProviderMessageSid is not null
                        && n.CreatedAt >= from && n.CreatedAt <= to
                        && n.Status != NotificationDeliveryStatus.Scheduled
                        && n.Status != NotificationDeliveryStatus.Canceled)
            .ToList();

        var providerBySid = providerMessages
            .GroupBy(p => p.ProviderMessageSid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopSent
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var provider in providerBySid)
        {
            if (eShopBySid.TryGetValue(provider.Key, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    provider.Key, provider.Value.Status, notification.Status, notification.Id, true, true));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(
                    provider.Key, provider.Value.Status, null, null, true, false));
            }
        }

        foreach (var notification in eShopBySid)
        {
            if (!providerBySid.ContainsKey(notification.Key))
            {
                eShopOnly.Add(new ReconciliationEntry(
                    notification.Key, null, notification.Value.Status, notification.Value.Id, false, true));
            }
        }

        return new ReconciliationReport(
            from,
            to,
            providerMessages.Count,
            eShopSent.Count,
            matched.Count,
            matched,
            providerOnly,
            eShopOnly);
    }
}
