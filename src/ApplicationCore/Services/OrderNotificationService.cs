using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS notifications around an order's lifecycle. Sending is best-effort: every provider
/// interaction is guarded so a failed message is recorded but never fails the order operation. Shopper phone
/// numbers and message bodies are treated as sensitive and are never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // "A few days later" for the how-did-delivery-go follow-up. Well inside the provider's 15-minute .. 35-day
    // scheduling window.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ISmsGateway _gateway;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ResendIdempotencyRecord> _idempotency;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsGateway gateway,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IRepository<ResendIdempotencyRecord> idempotency,
        IAppLogger<OrderNotificationService> logger)
    {
        _gateway = gateway;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _idempotency = idempotency;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(nameof(NotifyOrderPlacedAsync), order.Id, async () =>
        {
            var numbers = await GetNumbersAsync(order.BuyerId, cancellationToken);
            var body = $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us!";
            foreach (var number in numbers)
                await SendImmediateAsync(order, number, NotificationKind.OrderPlaced, body, cancellationToken);
        });
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(nameof(NotifyOrderDispatchedAsync), order.Id, async () =>
        {
            var numbers = await GetNumbersAsync(order.BuyerId, cancellationToken);
            var dispatchBody = $"Good news! Your eShop order #{order.Id} is on its way.";
            var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";
            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, number, NotificationKind.OrderDispatched, dispatchBody, cancellationToken);
                await ScheduleFollowUpAsync(order, number, followUpBody, sendAt, cancellationToken);
            }
        });
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(nameof(NotifyOrderCancelledAsync), order.Id, async () =>
        {
            // Call off any not-yet-sent follow-up FIRST, so it can never reach the shopper.
            var existing = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            foreach (var followUp in existing.Where(n => n.Kind == NotificationKind.DeliveryFollowUp && n.IsScheduledPending()))
            {
                try
                {
                    await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.MarkCanceled();
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    _logger.LogInformation("Cancelled scheduled follow-up notification {NotificationId} (sid {Sid}) for order {OrderId}.",
                        followUp.Id, followUp.ProviderMessageSid!, order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                        followUp.Id, order.Id, ex.Message);
                }
            }

            var numbers = await GetNumbersAsync(order.BuyerId, cancellationToken);
            var body = $"Your eShop order #{order.Id} has been cancelled. If this was unexpected, please contact support.";
            foreach (var number in numbers)
                await SendImmediateAsync(order, number, NotificationKind.OrderCancelled, body, cancellationToken);
        });
    }

    public async Task RefreshDeliveryStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications.Where(n => n.IsPending()))
        {
            try
            {
                var state = await _gateway.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh delivery state for notification {NotificationId}: {Error}",
                    notification.Id, ex.Message);
            }
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

        // Replay: same key returns the same result and sends nothing more.
        var prior = await _idempotency.FirstOrDefaultAsync(new ResendIdempotencyByKeySpecification(idempotencyKey), cancellationToken);
        if (prior is not null)
        {
            var priorResult = await _notifications.GetByIdAsync(prior.ResultNotificationId, cancellationToken)
                ?? throw new NotificationNotFoundException(prior.ResultNotificationId);
            _logger.LogInformation("Resend replayed under existing idempotency key; returning notification {NotificationId}.",
                priorResult.Id);
            return new ResendResult(priorResult, WasReplay: true);
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        if (source.ContentDisposed || string.IsNullOrEmpty(source.Body))
            throw new NotificationNotResendableException(notificationId, "its content has been disposed of");
        if (!source.IsUndelivered())
            throw new NotificationNotResendableException(notificationId, "it is not in a failed/undelivered state");

        var resent = new OrderNotification(source.OrderId, source.OwnerId, NotificationKind.Resend, source.ToNumber, source.Body!);
        try
        {
            var result = await _gateway.SendAsync(source.ToNumber, source.Body!, cancellationToken);
            resent.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            resent.RecordSendFailure(ex.Message);
            _logger.LogWarning("Resend of notification {SourceId} failed to hand off to provider: {Error}", source.Id, ex.Message);
        }

        resent = await _notifications.AddAsync(resent, cancellationToken);
        _logger.LogInformation("Resent notification {SourceId} as notification {NotificationId} (sid {Sid}, status {Status}).",
            source.Id, resent.Id, resent.ProviderMessageSid ?? "none", resent.DeliveryStatus ?? "unknown");

        // Record the key -> result mapping regardless of send outcome: repeating the same key must never
        // send again; a genuine retry uses a fresh key.
        await _idempotency.AddAsync(new ResendIdempotencyRecord(idempotencyKey, source.Id, resent.Id), cancellationToken);

        return new ResendResult(resent, WasReplay: false);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        var sid = notification.ProviderMessageSid;
        if (!string.IsNullOrEmpty(sid))
        {
            // Redact at the provider so the text is no longer retrievable there; the record survives.
            await _gateway.RedactBodyAsync(sid!, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content of notification {NotificationId} (sid {Sid}).",
            notification.Id, sid ?? "none");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must be on or after 'from'.", nameof(to));

        var providerMessages = await _gateway.ListSentFromConfiguredNumberAsync(from, to, cancellationToken);
        var eshopNotifications = await _notifications.ListAsync(new NotificationsInRangeSpecification(from, to), cancellationToken);

        var eshopBySid = eshopNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerSids = new HashSet<string>(
            providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!),
            StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();

        foreach (var message in providerMessages)
        {
            if (message.Sid is not null && eshopBySid.TryGetValue(message.Sid, out var matched))
            {
                lines.Add(new ReconciliationLine(message.Sid, ReconciliationSource.Matched,
                    message.Status, matched.DeliveryStatus, matched.Id, matched.OrderId, message.DateSent));
            }
            else
            {
                lines.Add(new ReconciliationLine(message.Sid, ReconciliationSource.ProviderOnly,
                    message.Status, null, null, null, message.DateSent));
            }
        }

        foreach (var notification in eshopNotifications.Where(n => n.ProviderMessageSid is not null && !providerSids.Contains(n.ProviderMessageSid!)))
        {
            lines.Add(new ReconciliationLine(notification.ProviderMessageSid, ReconciliationSource.EShopOnly,
                null, notification.DeliveryStatus, notification.Id, notification.OrderId, null));
        }

        var matchedCount = lines.Count(l => l.Source == ReconciliationSource.Matched);
        var providerOnly = lines.Count(l => l.Source == ReconciliationSource.ProviderOnly);
        var eshopOnly = lines.Count(l => l.Source == ReconciliationSource.EShopOnly);

        _logger.LogInformation(
            "Reconciliation {From}..{To}: provider={ProviderCount} eshop={EShopCount} matched={Matched} providerOnly={ProviderOnly} eshopOnly={EShopOnly}.",
            from, to, providerMessages.Count, eshopNotifications.Count, matchedCount, providerOnly, eshopOnly);

        return new ReconciliationReport(from, to, providerMessages.Count, eshopNotifications.Count,
            matchedCount, providerOnly, eshopOnly, lines);
    }

    private async Task<IReadOnlyList<ContactNumber>> GetNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
            _logger.LogInformation("No contact number on file for buyer of order; nothing to send.");
        return numbers;
    }

    private async Task SendImmediateAsync(Order order, ContactNumber number, NotificationKind kind, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber, body);
        try
        {
            var result = await _gateway.SendAsync(number.PhoneNumber, body, cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
            _logger.LogInformation("Sent {Kind} for order {OrderId} as notification (sid {Sid}, status {Status}).",
                kind, order.Id, result.Sid, result.Status ?? "unknown");
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Failed to send {Kind} for order {OrderId}: {Error}", kind, order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, ContactNumber number, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
        try
        {
            var result = await _gateway.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
            notification.RecordScheduled(result.Sid, result.Status, result.ScheduledAt ?? sendAt);
            _logger.LogInformation("Scheduled delivery follow-up for order {OrderId} (sid {Sid}) for {SendAt}.",
                order.Id, result.Sid, sendAt);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure(ex.Message);
            _logger.LogWarning("Failed to schedule delivery follow-up for order {OrderId}: {Error}", order.Id, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    /// <summary>Runs a notification side-effect so that no failure in it can surface to the order operation.</summary>
    private async Task SafelyAsync(string operation, int orderId, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("{Operation} for order {OrderId} did not complete cleanly: {Error}", operation, orderId, ex.Message);
        }
    }
}
