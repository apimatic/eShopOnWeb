using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperationsService : INotificationOperationsService
{
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<NotificationOperationsService> _logger;

    public NotificationOperationsService(
        IRepository<Notification> notifications,
        ISmsProvider smsProvider,
        IAppLogger<NotificationOperationsService> logger)
    {
        _notifications = notifications;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Repeating a request under the same key must not send a second message.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend for notification {NotificationId} short-circuited by idempotency key; returning {ResultId}.",
                notificationId, alreadyDone.Id);
            return alreadyDone.Id;
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationContentDisposedException(notificationId);
        }

        // A resend produces a NEW message/notification, tied to the same order and recipient.
        var resend = new Notification(original.BuyerId, original.OrderId, original.Type, original.ToNumber, original.Body);
        resend.AssignIdempotencyKey(idempotencyKey);
        await _notifications.AddAsync(resend, ct);

        try
        {
            var result = await _smsProvider.SendAsync(resend.ToNumber, resend.Body!, ct);
            resend.RecordSent(result.ProviderSid, result.Status, result.ErrorCode, result.ErrorMessage);
            _logger.LogInformation("Notification {OriginalId} re-sent as {ResendId} with status {Status}.",
                notificationId, resend.Id, resend.DeliveryStatus);
        }
        catch (SmsProviderException ex)
        {
            // The key is consumed regardless of outcome; a genuine retry uses a fresh key.
            resend.RecordSendFailure(ex.Message);
            _logger.LogWarning("Resend of notification {OriginalId} (as {ResendId}) could not be sent: {Reason}",
                notificationId, resend.Id, ex.Message);
        }

        await _notifications.UpdateAsync(resend, ct);
        return resend.Id;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        if (notification.ContentDisposed)
        {
            return; // Already disposed — nothing further to do.
        }

        // Remove the text at the provider FIRST, so it is genuinely no longer retrievable there and
        // not merely hidden by this application. If that fails, do not clear it locally.
        if (notification.ProviderMessageSid is not null)
        {
            await _smsProvider.RedactContentAsync(notification.ProviderMessageSid, ct);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, ct);
        _logger.LogInformation("Notification {NotificationId} content disposed; record and status retained.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Ask the provider for its own record of messages from the configured sending number (whole range).
        var providerMessages = await _smsProvider.ListSentMessagesAsync(from, to, ct);

        // What eShop believes it sent in the range (records carrying a provider message id).
        var eShopNotifications = await _notifications.ListAsync(new SentNotificationsInRangeSpecification(from, to), ct);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.ProviderSid))
            .GroupBy(m => m.ProviderSid)
            .ToDictionary(g => g.Key, g => g.First());

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, providerMessage) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(sid, providerMessage.Status, notification.DeliveryStatus, notification.Id));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(sid, providerMessage.Status, null, null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(new ReconciliationEntry(sid, null, notification.DeliveryStatus, notification.Id));
            }
        }

        _logger.LogInformation("Reconciliation {From}..{To}: {Matched} matched, {ProviderOnly} provider-only, {EShopOnly} eShop-only.",
            from, to, matched.Count, providerOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }
}
