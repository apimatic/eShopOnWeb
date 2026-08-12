using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Operator actions on individual notifications: resend, content disposal, reconciliation.</summary>
public class NotificationAdminService : INotificationAdminService
{
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsSender _sms;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<Notification> notifications,
        ISmsSender sms,
        IAppLogger<NotificationAdminService> logger)
    {
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new ResendResult(ResendOutcome.Invalid, 0, null, "An idempotency key is required.");
        }

        // Under a key already used, return the message that first request produced — do not send again.
        var priorForKey = await _notifications.ListAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        var replay = priorForKey.FirstOrDefault();
        if (replay is not null)
        {
            return new ResendResult(ResendOutcome.ReplayedIdempotent, replay.Id, replay.Status, null);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotFound, 0, null, "Notification not found.");
        }

        var body = original.Body;
        if (string.IsNullOrEmpty(body))
        {
            return new ResendResult(ResendOutcome.Invalid, 0, null,
                "The original message content is unavailable (it may have been disposed of) and cannot be resent.");
        }

        var resend = new Notification(original.BuyerId, original.OrderId, NotificationKind.Resend,
            original.ToNumber, body, idempotencyKey: idempotencyKey);
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _sms.SendAsync(original.ToNumber, body, cancellationToken);
            resend.ApplyProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed("Provider send failed.");
            _logger.LogWarning("Resend failed for notification {0}: {1}", resend.Id, ex.GetType().Name);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return new ResendResult(ResendOutcome.Sent, resend.Id, resend.Status, null);
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return new ContentDisposalResult(ContentDisposalOutcome.NotFound, "Notification not found.");
        }

        // The content must no longer be retrievable from the provider either — redact there first, and
        // only clear the local copy if that succeeded.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _sms.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider redaction failed for notification {0}: {1}", notification.Id, ex.GetType().Name);
                return new ContentDisposalResult(ContentDisposalOutcome.ProviderFailed,
                    "The message content could not be disposed of at the provider.");
            }
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return new ContentDisposalResult(ContentDisposalOutcome.Disposed, null);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _sms.ListSentMessagesAsync(from, to, cancellationToken);
        var eShopNotifications = await _notifications.ListAsync(new NotificationsWithProviderSidBetweenSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .GroupBy(m => m.MessageSid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(sid, notification.Id, message.Status, notification.Status, message.DateSent));
            }
            else
            {
                // The provider knows about this message but eShop does not.
                providerOnly.Add(new ReconciliationEntry(sid, null, message.Status, null, message.DateSent));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                // eShop believes it sent this but the provider does not report it.
                eShopOnly.Add(new ReconciliationEntry(sid, notification.Id, null, notification.Status, null));
            }
        }

        return new ReconciliationReport(
            from, to, _sms.SendingNumber,
            providerMessages.Count, eShopNotifications.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }
}
