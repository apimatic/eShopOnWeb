using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends and tracks the SMS notifications that go out as an order moves. Every send is best-effort:
/// a provider failure is recorded on the notification and never propagated, so it can never fail the
/// order operation that triggered it. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsGateway _gateway;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsGateway gateway,
        TwilioSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderPlaced, number, PlacedBody(order.Id), cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        var sendAt = DateTimeOffset.UtcNow.Add(_settings.FollowUpDelay);

        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderDispatched, number, DispatchedBody(order.Id), cancellationToken);
            await ScheduleFollowUpAsync(order, number, FollowUpBody(order.Id), sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First, call off any delivery follow-up still queued with the provider: a cancelled order
        // must never trigger a "how did delivery go?" message.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderCancelled, number, CancelledBody(order.Id), cancellationToken);
        }
    }

    public async Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message the first attempt produced,
        // without sending a second message.
        var existing = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("Resend idempotency-key already seen; returning existing notification {NotificationId}", existing.Id);
            return existing;
        }

        var original = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (original == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }
        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            throw new NotificationContentUnavailableException(notificationId);
        }

        var resend = new Notification(original.BuyerId, original.OrderId, NotificationKind.Resend, original.ToNumber, original.Body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        try
        {
            var result = await _gateway.SendAsync(original.ToNumber, original.Body!, cancellationToken);
            resend.RecordSent(result.Sid, result.Status);
            _logger.LogInformation("Resent notification {OriginalId} as {ResendSid} (status {Status})", original.Id, result.Sid, result.Status);
        }
        catch (Exception ex)
        {
            resend.RecordSendFailed();
            _logger.LogWarning("Resend of notification {OriginalId} could not be handed to the provider: {Error}", original.Id, ex.Message);
        }

        // Persist regardless of send outcome so the idempotency key is recorded and repeats do not re-send.
        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (notification == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        // Dispose of the content on the provider's side first; only then record it locally. If the
        // provider call fails we do NOT mark it redacted, so the caller learns it did not take effect.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _gateway.RedactContentAsync(notification.ProviderMessageSid!, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId} (sid {Sid})", notification.Id, notification.ProviderMessageSid);
    }

    public async Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || MessageStatuses.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var status = await _gateway.GetStatusAsync(notification.ProviderMessageSid!, cancellationToken);
                if (!string.Equals(status, notification.Status, StringComparison.OrdinalIgnoreCase))
                {
                    notification.UpdateStatus(status);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // A read must not fail because a status refresh could not reach the provider.
                _logger.LogWarning("Could not refresh status for notification {NotificationId} (sid {Sid}): {Error}",
                    notification.Id, notification.ProviderMessageSid, ex.Message);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _gateway.ListSentMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notifications.ListAsync(new NotificationsSentInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ReconciliationEntry>();

        foreach (var pm in providerMessages)
        {
            if (string.IsNullOrEmpty(pm.Sid))
            {
                continue;
            }
            providerSids.Add(pm.Sid);

            if (localBySid.TryGetValue(pm.Sid, out var local))
            {
                entries.Add(new ReconciliationEntry(pm.Sid, ReconciliationOutcome.Matched, pm.Status, local.Status, local.Id, local.OrderId, pm.DateSent));
            }
            else
            {
                entries.Add(new ReconciliationEntry(pm.Sid, ReconciliationOutcome.ProviderOnly, pm.Status, null, null, null, pm.DateSent));
            }
        }

        foreach (var local in localBySid.Values)
        {
            if (!providerSids.Contains(local.ProviderMessageSid!))
            {
                entries.Add(new ReconciliationEntry(local.ProviderMessageSid, ReconciliationOutcome.EShopOnly, null, local.Status, local.Id, local.OrderId, null));
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Entries = entries,
            MatchedCount = entries.Count(e => e.Outcome == ReconciliationOutcome.Matched),
            ProviderOnlyCount = entries.Count(e => e.Outcome == ReconciliationOutcome.ProviderOnly),
            EShopOnlyCount = entries.Count(e => e.Outcome == ReconciliationOutcome.EShopOnly)
        };
    }

    // ---- helpers ----

    private async Task<IReadOnlyList<string>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(c => c.PhoneNumber).ToList();
    }

    private async Task SendImmediateAsync(Order order, NotificationKind kind, string toNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.BuyerId, order.Id, kind, toNumber, body);
        try
        {
            var result = await _gateway.SendAsync(toNumber, body, cancellationToken);
            notification.RecordSent(result.Sid, result.Status);
            _logger.LogInformation("Sent {Kind} notification for order {OrderId} (sid {Sid}, status {Status})", kind, order.Id, result.Sid, result.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailed();
            _logger.LogWarning("{Kind} notification for order {OrderId} could not be handed to the provider: {Error}", kind, order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new Notification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp, toNumber, body);
        try
        {
            var result = await _gateway.ScheduleAsync(toNumber, body, sendAt, cancellationToken);
            notification.RecordScheduled(result.Sid, result.Status, sendAt);
            _logger.LogInformation("Queued delivery follow-up for order {OrderId} (sid {Sid}, sendAt {SendAt:o}, status {Status})", order.Id, result.Sid, sendAt, result.Status);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailed();
            _logger.LogWarning("Delivery follow-up for order {OrderId} could not be queued with the provider: {Error}", order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled follow-up {NotificationId} for order {OrderId} (sid {Sid})", followUp.Id, orderId, followUp.ProviderMessageSid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not call off scheduled follow-up {NotificationId} for order {OrderId} (sid {Sid}): {Error}",
                    followUp.Id, orderId, followUp.ProviderMessageSid, ex.Message);
            }
        }
    }

    private static string PlacedBody(int orderId) => $"eShop: your order #{orderId} has been placed. Thank you for shopping with us!";
    private static string DispatchedBody(int orderId) => $"eShop: good news - your order #{orderId} is on its way!";
    private static string FollowUpBody(int orderId) => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.";
    private static string CancelledBody(int orderId) => $"eShop: your order #{orderId} has been cancelled. Contact support if this is unexpected.";
}
