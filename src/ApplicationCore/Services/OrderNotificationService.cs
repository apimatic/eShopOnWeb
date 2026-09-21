using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends the SMS notifications that accompany an order's lifecycle and owns resend, content disposal and
/// reconciliation. Sending failures are recorded on the notification and never propagated out of the
/// notify methods, so the underlying order operation always succeeds.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider delivery statuses that will not change again — no point re-reading them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "received", "read"
    };

    private readonly IRepository<Notification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsProvider _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Notification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ISmsProvider sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Total {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you!";
        await SendToEachRegisteredNumberAsync(order.BuyerId, order.Id, NotificationKind.OrderPlaced, body, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct)
    {
        var dispatchBody = $"eShop: good news! Your order #{order.Id} is on its way.";
        await SendToEachRegisteredNumberAsync(order.BuyerId, order.Id, NotificationKind.OrderDispatched, dispatchBody, ct);

        // Queue a follow-up with the provider for a few days out — not held in this app on a timer.
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? Reply with your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        foreach (var number in numbers)
        {
            var n = new Notification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp,
                number.E164Number, followUpBody, scheduledSendAt: sendAt);
            await _notifications.AddAsync(n, ct);
            try
            {
                var sent = await _sms.ScheduleAsync(number.E164Number, followUpBody, sendAt, ct);
                n.RecordSent(sent.ProviderSid, sent.Status, sent.DateSent, sent.ErrorCode, sent.ErrorMessage);
                _logger.LogInformation($"Scheduled follow-up notification {n.Id} for order {order.Id} (sid {sent.ProviderSid}, status {sent.Status}).");
            }
            catch (SmsProviderException ex)
            {
                RecordProviderFailure(n, ex);
                _logger.LogWarning($"Follow-up notification {n.Id} for order {order.Id} could not be scheduled: {ex.Message}");
            }
            await _notifications.UpdateAsync(n, ct);
        }
    }

    public async Task NotifyOrderCanceledAsync(Order order, CancellationToken ct)
    {
        // Call off any not-yet-sent follow-up first, so a "how did delivery go?" for a cancelled order can never go out.
        var pending = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), ct);
        foreach (var follow in pending)
        {
            if (string.IsNullOrEmpty(follow.ProviderMessageSid)) continue;
            try
            {
                var res = await _sms.CancelScheduledAsync(follow.ProviderMessageSid, ct);
                follow.RecordCanceled(res.Status);
                _logger.LogInformation($"Called off scheduled follow-up {follow.Id} for cancelled order {order.Id} (status {res.Status}).");
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning($"Could not call off follow-up {follow.Id} for order {order.Id}: {ex.Message}");
            }
            await _notifications.UpdateAsync(follow, ct);
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. No further action is needed.";
        await SendToEachRegisteredNumberAsync(order.BuyerId, order.Id, NotificationKind.OrderCanceled, body, ct);
    }

    public async Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required for a resend.", nameof(idempotencyKey));

        // Same key seen before → return what it already produced; do not send a second message.
        var already = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (already is not null) return already;

        var original = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
            throw new InvalidOperationException("The message content has been disposed of and cannot be resent.");

        var resend = new Notification(original.OwnerId, original.OrderId, NotificationKind.Resend,
            original.ToNumber, original.Body!, idempotencyKey: idempotencyKey);

        try
        {
            await _notifications.AddAsync(resend, ct);
        }
        catch (Exception) // unique-key violation from a concurrent resend under the same key
        {
            var winner = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
            if (winner is not null && winner.Id != resend.Id) return winner;
            throw;
        }

        try
        {
            var sent = await _sms.SendAsync(resend.ToNumber, original.Body!, ct);
            resend.RecordSent(sent.ProviderSid, sent.Status, sent.DateSent, sent.ErrorCode, sent.ErrorMessage);
            _logger.LogInformation($"Resent notification {resend.Id} for order {resend.OrderId} (sid {sent.ProviderSid}, status {sent.Status}).");
        }
        catch (SmsProviderException ex)
        {
            RecordProviderFailure(resend, ex);
            _logger.LogWarning($"Resend {resend.Id} for order {resend.OrderId} failed to send: {ex.Message}");
        }
        await _notifications.UpdateAsync(resend, ct);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var n = await _notifications.GetByIdAsync(notificationId, ct)
            ?? throw new KeyNotFoundException($"Notification {notificationId} was not found.");

        if (!string.IsNullOrEmpty(n.ProviderMessageSid))
        {
            // Redact at the provider so the text is no longer retrievable there, while the record survives.
            await _sms.RedactAsync(n.ProviderMessageSid, ct);
        }
        n.MarkContentRedacted();
        await _notifications.UpdateAsync(n, ct);
        _logger.LogInformation($"Disposed of content for notification {notificationId} (order {n.OrderId}).");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Make local state current so both sides reflect the provider's send timestamp.
        var withSid = await _notifications.ListAsync(new NotificationsWithProviderSidSpecification(), ct);
        await RefreshStatusesAsync(withSid, ct);

        var providerMsgs = await _sms.ListSentFromConfiguredNumberAsync(from, to, ct);

        // Both sides filtered on the provider's send timestamp — never a local row-creation column.
        var providerBySid = providerMsgs
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var localBySid = withSid
            .Where(n => n.ProviderMessageSid is not null
                        && n.ProviderDateSent is not null
                        && n.ProviderDateSent >= from && n.ProviderDateSent <= to)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, msg) in providerBySid)
        {
            if (localBySid.TryGetValue(sid, out var local))
                matched.Add(new ReconciliationEntry(sid, msg.Status, local.ProviderStatus, local.Id, msg.DateSent));
            else
                providerOnly.Add(new ReconciliationEntry(sid, msg.Status, null, null, msg.DateSent));
        }
        foreach (var (sid, local) in localBySid)
        {
            if (!providerBySid.ContainsKey(sid))
                eShopOnly.Add(new ReconciliationEntry(sid, null, local.ProviderStatus, local.Id, local.ProviderDateSent));
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: provider={providerBySid.Count}, local={localBySid.Count}, matched={matched.Count}, providerOnly={providerOnly.Count}, eShopOnly={eShopOnly.Count}.");

        return new ReconciliationReport(from, to, providerBySid.Count, localBySid.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }

    public async Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken ct)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderMessageSid)) continue;
            if (n.ProviderStatus is not null && TerminalStatuses.Contains(n.ProviderStatus)) continue;

            try
            {
                var latest = await _sms.FetchAsync(n.ProviderMessageSid, ct);
                if (latest is not null)
                {
                    n.RefreshProviderState(latest.Status, latest.DateSent, latest.ErrorCode, latest.ErrorMessage);
                    await _notifications.UpdateAsync(n, ct);
                }
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning($"Could not refresh provider state for notification {n.Id}: {ex.Message}");
            }
        }
    }

    private async Task SendToEachRegisteredNumberAsync(string ownerId, int orderId, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation($"No contact number on file for order {orderId}; {kind} notification skipped.");
            return;
        }

        foreach (var number in numbers)
        {
            var n = new Notification(ownerId, orderId, kind, number.E164Number, body);
            await _notifications.AddAsync(n, ct); // local record first (Pending), then the provider call
            try
            {
                var sent = await _sms.SendAsync(number.E164Number, body, ct);
                n.RecordSent(sent.ProviderSid, sent.Status, sent.DateSent, sent.ErrorCode, sent.ErrorMessage);
                _logger.LogInformation($"Sent {kind} notification {n.Id} for order {orderId} (sid {sent.ProviderSid}, status {sent.Status}).");
            }
            catch (SmsProviderException ex)
            {
                RecordProviderFailure(n, ex);
                _logger.LogWarning($"{kind} notification {n.Id} for order {orderId} failed to send: {ex.Message}");
            }
            await _notifications.UpdateAsync(n, ct);
        }
    }

    private static void RecordProviderFailure(Notification n, SmsProviderException ex)
    {
        if (ex.OutcomeUnknown)
            n.RecordUnknown(ex.Message);
        else
            n.RecordFailed(null, (int?)ex.StatusCode, ex.Message);
    }
}
