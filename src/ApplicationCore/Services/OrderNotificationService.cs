using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Turns order events into SMS to the shopper's numbers on file, and owns the follow-up lifecycle.
/// Every notify path is best-effort: a send that cannot go out is recorded on the notification and never
/// propagates, so the order operation always succeeds. Phone numbers are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did the delivery go?" follow-up is queued — a few days, per the brief.</summary>
    private static readonly TimeSpan FeedbackDelay = TimeSpan.FromDays(3);

    /// <summary>One budget for a whole notify/report operation so a stalled provider cannot pin the request.</summary>
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);

    private readonly ISmsSender _sms;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsSender sms,
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IAppLogger<OrderNotificationService> logger)
    {
        _sms = sms;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        foreach (var number in await NumbersFor(order.BuyerId, cts.Token))
        {
            var notification = new OrderNotification(order.BuyerId, order.Id, NotificationType.OrderPlaced,
                number.PhoneNumber, $"Your eShop order #{order.Id} has been placed. Thank you!");
            await SaveNewAndSend(notification, cts.Token);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var sendAt = DateTimeOffset.UtcNow.Add(FeedbackDelay);

        foreach (var number in await NumbersFor(order.BuyerId, cts.Token))
        {
            // 1. It's on its way — immediate.
            var dispatched = new OrderNotification(order.BuyerId, order.Id, NotificationType.OrderDispatched,
                number.PhoneNumber, $"Good news! Your eShop order #{order.Id} is on its way.");
            await SaveNewAndSend(dispatched, cts.Token);

            // 2. How did the delivery go? — queued WITH the provider for a few days later, not held here.
            var feedback = new OrderNotification(order.BuyerId, order.Id, NotificationType.DeliveryFeedback,
                number.PhoneNumber, $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.",
                isScheduled: true, scheduledSendAt: sendAt);
            await _notifications.AddAsync(feedback, cts.Token);
            await TryScheduleAsync(feedback, sendAt, cts.Token);
            await _notifications.UpdateAsync(feedback, cts.Token);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);

        // First, call off any follow-up that has not yet gone out — a "how was delivery?" text for a cancelled
        // order is exactly the incident this prevents.
        var pending = await _notifications.ListAsync(new PendingFeedbackByOrderSpecification(order.Id), cts.Token);
        foreach (var followUp in pending)
        {
            if (followUp.ProviderMessageSid is null)
            {
                followUp.MarkCanceled();
            }
            else
            {
                try
                {
                    await _sms.CancelScheduledAsync(followUp.ProviderMessageSid, cts.Token);
                    followUp.MarkCanceled();
                }
                catch (SmsProviderException ex)
                {
                    _logger.LogWarning("Could not cancel scheduled follow-up for order #{0}: {1}", order.Id, ex.Message);
                }
            }
            await _notifications.UpdateAsync(followUp, cts.Token);
        }

        // Then tell the shopper the order was cancelled.
        foreach (var number in await NumbersFor(order.BuyerId, cts.Token))
        {
            var notification = new OrderNotification(order.BuyerId, order.Id, NotificationType.OrderCancelled,
                number.PhoneNumber, $"Your eShop order #{order.Id} has been cancelled.");
            await SaveNewAndSend(notification, cts.Token);
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        using var cts = Budget(cancellationToken);

        // Idempotency: a resend already produced under this key returns that same message, without sending again.
        var already = await _notifications.FirstOrDefaultAsync(new ResendByIdempotencyKeySpecification(idempotencyKey), cts.Token);
        if (already is not null)
        {
            return already;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cts.Token);
        Guard.Against.Null(original, nameof(original));

        if (string.IsNullOrEmpty(original.Body))
        {
            throw new SmsProviderException("The message content has been disposed of and cannot be resent.",
                System.Net.HttpStatusCode.Conflict);
        }

        var resend = new OrderNotification(original.BuyerId, original.OrderId, NotificationType.Resend,
            original.ToNumber, original.Body, idempotencyKey: idempotencyKey);

        // Persist the keyed record BEFORE sending so a concurrent repeat under the same key finds it.
        await _notifications.AddAsync(resend, cts.Token);
        await TrySendAsync(resend, cts.Token);
        await _notifications.UpdateAsync(resend, cts.Token);
        return resend;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var notification = await _notifications.GetByIdAsync(notificationId, cts.Token);
        Guard.Against.Null(notification, nameof(notification));

        // Dispose at the provider FIRST. Only redact locally once the provider confirms, so a provider-side
        // failure surfaces (and can be retried) rather than us claiming the text is gone when it isn't.
        if (notification.ProviderMessageSid is not null && !notification.ContentRedacted)
        {
            await _sms.RedactBodyAsync(notification.ProviderMessageSid, cts.Token);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cts.Token);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);

        var providerMessages = await _sms.ListSentMessagesAsync(fromUtc, toUtc, cts.Token);

        // eShop's side is the traffic that goes out from the configured sending number — the immediate sends.
        // Scheduled follow-ups go via the messaging service (no single From) and are out of scope for a From-filtered report.
        var eshop = (await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(fromUtc, toUtc), cts.Token))
            .Where(n => !n.IsScheduled)
            .ToList();

        var eshopBySid = new Dictionary<string, OrderNotification>();
        foreach (var n in eshop.Where(n => n.ProviderMessageSid is not null))
        {
            eshopBySid[n.ProviderMessageSid!] = n;
        }
        var providerSids = new HashSet<string>(providerMessages.Where(m => m.Sid is not null).Select(m => m.Sid!));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var message in providerMessages)
        {
            if (message.Sid is not null && eshopBySid.TryGetValue(message.Sid, out var n))
            {
                matched.Add(new ReconciliationEntry
                {
                    Sid = message.Sid,
                    NotificationId = n.Id,
                    OrderId = n.OrderId,
                    ProviderStatus = message.Status,
                    EShopState = n.SendState.ToString(),
                    DateSent = message.DateSent
                });
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry
                {
                    Sid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent
                });
            }
        }

        var eshopOnly = eshop
            .Where(n => n.ProviderMessageSid is null || !providerSids.Contains(n.ProviderMessageSid))
            .Select(n => new ReconciliationEntry
            {
                Sid = n.ProviderMessageSid,
                NotificationId = n.Id,
                OrderId = n.OrderId,
                ProviderStatus = n.ProviderStatus,
                EShopState = n.SendState.ToString()
            })
            .ToList();

        return new ReconciliationReport
        {
            From = fromUtc,
            To = toUtc,
            FromNumber = _sms.SendingNumber,
            ProviderCount = providerMessages.Count,
            EShopCount = eshop.Count,
            MatchedCount = matched.Count,
            Matched = matched,
            ProviderOnly = providerOnly,
            EShopOnly = eshopOnly
        };
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var list = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cts.Token);

        // Refresh the provider-owned delivery outcome so the caller sees where each message actually got to.
        foreach (var n in list)
        {
            if (n.ProviderMessageSid is null || n.SendState == NotificationSendState.Canceled)
            {
                continue;
            }
            try
            {
                var status = await _sms.FetchStatusAsync(n.ProviderMessageSid, cts.Token);
                n.UpdateDeliveryStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notifications.UpdateAsync(n, cts.Token);
            }
            catch (SmsProviderException ex)
            {
                _logger.LogWarning("Could not refresh status for notification #{0}: {1}", n.Id, ex.Message);
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<ContactNumber>> NumbersFor(string buyerId, CancellationToken ct)
        => await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    private async Task SaveNewAndSend(OrderNotification notification, CancellationToken ct)
    {
        await _notifications.AddAsync(notification, ct);
        await TrySendAsync(notification, ct);
        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task TrySendAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            var result = await _sms.SendAsync(notification.ToNumber, notification.Body!, ct);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                notification.RecordSendIndeterminate(ex.Message);
            }
            else
            {
                notification.RecordSendFailure(null, ex.Message);
            }
            _logger.LogWarning("Order #{0} {1} SMS not sent: {2}", notification.OrderId, notification.Type, ex.Message);
        }
    }

    private async Task TryScheduleAsync(OrderNotification notification, DateTimeOffset sendAt, CancellationToken ct)
    {
        try
        {
            var result = await _sms.ScheduleAsync(notification.ToNumber, notification.Body!, sendAt, ct);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (SmsProviderException ex)
        {
            notification.RecordSendFailure(null, ex.Message);
            _logger.LogWarning("Order #{0} delivery-feedback follow-up not scheduled: {1}", notification.OrderId, ex.Message);
        }
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(OperationBudget);
        return cts;
    }
}
