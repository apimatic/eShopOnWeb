using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The operator- and reporting-facing operations over already-sent messages: refresh their delivery
/// outcomes from the provider, re-send one that did not reach the shopper (idempotently), dispose of a
/// message's content, and reconcile the provider's records against this application's.
/// </summary>
public class SmsNotificationService : ISmsNotificationService
{
    /// <summary>Provider statuses that mean the message actually went out (so eShop believes it sent it).</summary>
    private static readonly HashSet<string> SentStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "sent", "sending", "delivered", "undelivered", "receiving", "received", "read", "partially_delivered"
    };

    private readonly IRepository<SmsNotification> _notifications;
    private readonly IReadRepository<Order> _orders;
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<SmsNotificationService> _logger;

    public SmsNotificationService(
        IRepository<SmsNotification> notifications,
        IReadRepository<Order> orders,
        ISmsGateway gateway,
        IAppLogger<SmsNotificationService> logger)
    {
        _notifications = notifications;
        _orders = orders;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task RefreshDeliveryOutcomesAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var n in notifications)
        {
            if (!n.WasCreatedWithProvider || n.IsTerminal) continue;
            try
            {
                var result = await _gateway.GetStatusAsync(n.ProviderSid!, cancellationToken);
                n.UpdateDeliveryOutcome(result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
                await _notifications.UpdateAsync(n, cancellationToken);
            }
            catch (Exception ex)
            {
                // Leave the stored outcome as-is; reporting still works with the last-known value.
                _logger.LogWarning($"Could not refresh delivery outcome for notification {n.Id} ({n.ProviderSid}): {PhoneNumberRedactor.Scrub(ex.Message)}");
            }
        }
    }

    public async Task<SmsNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a request already handled under this key returns the message it produced, so a
        // repeat never sends a second message. A genuine second attempt uses a fresh key.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(
            new SmsNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            _logger.LogInformation($"Resend under existing idempotency key returned notification {alreadyDone.Id}; no new message sent.");
            return alreadyDone;
        }

        var original = await _notifications.FirstOrDefaultAsync(
            new SmsNotificationByIdSpecification(notificationId), cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        var body = await ResolveBodyAsync(original, cancellationToken);
        var resend = new SmsNotification(
            original.OwnerId, original.OrderId, original.Kind, original.ToPhoneNumber, body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);

        try
        {
            var result = await _gateway.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
        }
        catch (SmsGatewayException ex)
        {
            resend.RecordProviderResult(null, "failed", ex.ProviderErrorCode, ex.Message, null);
            _logger.LogWarning($"Resend of notification {notificationId} was refused by the provider (code {ex.ProviderErrorCode}).");
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(
            new SmsNotificationByIdSpecification(notificationId), cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (notification.ContentDisposed)
            return; // already disposed; idempotent

        // Dispose at the provider first: only once the text is gone there do we claim it is gone. If the
        // message never reached the provider there is nothing to dispose of there.
        if (notification.WasCreatedWithProvider)
        {
            await _gateway.DisposeContentAsync(notification.ProviderSid!, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Content of notification {notificationId} ({notification.ProviderSid}) was disposed of.");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for messages sent from this application's own number over the range.
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to, cancellationToken);

        // What this application believes it sent over the range (by when each record was created).
        var local = await _notifications.ListAsync(new SmsNotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var localBySid = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var pm in providerMessages)
        {
            if (localBySid.TryGetValue(pm.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry(pm.Sid, pm.Status, pm.DateSent, n.Id, n.OrderId, n.ProviderStatus));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(pm.Sid, pm.Status, pm.DateSent, null, null, null));
            }
        }

        // "eShop-only" is a genuine integrity gap: a message eShop believes it SENT that the provider has
        // no sent record of. Messages that were never sent (scheduled, then cancelled, or that failed
        // before reaching the provider) are excluded — they are not something the provider should be
        // reporting as sent.
        var eShopOnly = local
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid)
                        && n.ProviderStatus is not null && SentStatuses.Contains(n.ProviderStatus)
                        && !providerSids.Contains(n.ProviderSid!))
            .Select(n => new ReconciliationEntry(n.ProviderSid, null, n.SentAt, n.Id, n.OrderId, n.ProviderStatus))
            .ToList();

        return new ReconciliationReport(from, to, _gateway.SendingNumber, matched, providerOnly, eShopOnly);
    }

    private async Task<string> ResolveBodyAsync(SmsNotification original, CancellationToken ct)
    {
        // Reuse the original text when we still hold it; otherwise recompose it from the order so a
        // resend of a message whose content was disposed of still carries a sensible message.
        if (!string.IsNullOrEmpty(original.Body))
            return original.Body!;

        var order = await _orders.GetByIdAsync(original.OrderId, ct);
        return order is not null
            ? NotificationMessages.For(original.Kind, order)
            : $"eShopOnWeb: an update about your order #{original.OrderId}.";
    }
}
