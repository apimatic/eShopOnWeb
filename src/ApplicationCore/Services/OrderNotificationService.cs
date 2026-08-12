using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the "how did the delivery go?" follow-up is queued for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IReadRepository<Order> _orders;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsProvider smsProvider,
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        IReadRepository<Order> orders,
        IAppLogger<OrderNotificationService> logger)
    {
        _smsProvider = smsProvider;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetNumbersAsync(order, cancellationToken);
        if (numbers.Count == 0) return;

        var body = BuildBody(NotificationKind.OrderPlaced, order);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderPlaced, number.PhoneNumber, body, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetNumbersAsync(order, cancellationToken);
        if (numbers.Count == 0) return;

        var dispatchBody = BuildBody(NotificationKind.OrderDispatched, order);
        var followUpBody = BuildBody(NotificationKind.DeliveryFollowUp, order);
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            // 1) Tell the shopper it is on its way, now.
            await SendImmediateAsync(order, NotificationKind.OrderDispatched, number.PhoneNumber, dispatchBody, cancellationToken);

            // 2) Queue the delivery follow-up WITH THE PROVIDER for a few days later — not held here.
            var followUp = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber);
            try
            {
                var result = await _smsProvider.ScheduleAsync(number.PhoneNumber, followUpBody, sendAt, cancellationToken);
                followUp.MarkScheduled(result.Sid, result.Status, sendAt, result.ErrorCode);
            }
            catch (Exception ex)
            {
                followUp.MarkScheduled(null, NotificationDeliveryStatus.SendError, sendAt, null);
                _logger.LogWarning("Order {OrderId}: delivery follow-up could not be scheduled with the provider ({Error}); dispatch still succeeds.",
                    order.Id, Describe(ex));
            }
            await _notifications.AddAsync(followUp, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First priority: call off any not-yet-sent delivery follow-up so it can NEVER reach the shopper.
        var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var result = await _smsProvider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryOutcome(result.Status, result.ErrorCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: failed to cancel scheduled follow-up {Sid} ({Error}).",
                    order.Id, followUp.ProviderMessageSid, Describe(ex));
            }
            await _notifications.UpdateAsync(followUp, cancellationToken);
        }

        // Then tell the shopper the order was cancelled.
        var numbers = await GetNumbersAsync(order, cancellationToken);
        if (numbers.Count == 0) return;

        var body = BuildBody(NotificationKind.OrderCancelled, order);
        foreach (var number in numbers)
        {
            await SendImmediateAsync(order, NotificationKind.OrderCancelled, number.PhoneNumber, body, cancellationToken);
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key replays the prior result, never sends again.
        var prior = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (prior is not null)
        {
            return ResendResult.Replayed(prior);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null) return ResendResult.Missing();

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(original.OrderId), cancellationToken);
        if (order is null) return ResendResult.Missing();

        // Act on current state: refresh the original's delivery outcome first (best-effort).
        await RefreshDeliveryOutcomesAsync(new[] { original }, cancellationToken);

        var body = BuildBody(original.Kind, order);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Kind, original.ToPhoneNumber);
        resend.FlagAsResend(idempotencyKey);
        try
        {
            var result = await _smsProvider.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordSendResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            resend.RecordSendResult(null, NotificationDeliveryStatus.SendError, null);
            _logger.LogWarning("Resend of notification {NotificationId} could not be handed to the provider ({Error}).",
                notificationId, Describe(ex));
        }
        await _notifications.AddAsync(resend, cancellationToken);
        return ResendResult.Sent(resend);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null) return false;

        // The text must no longer be retrievable FROM THE PROVIDER — redact it there. If the provider
        // cannot redact, we must not claim success, so the exception propagates and nothing is marked.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}; the send record survives.", notificationId);
        return true;
    }

    public async Task RefreshDeliveryOutcomesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid)) continue;
            try
            {
                var current = await _smsProvider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                var statusChanged = !string.Equals(notification.DeliveryStatus, current.Status, StringComparison.OrdinalIgnoreCase);
                var codeChanged = notification.ProviderErrorCode != current.ErrorCode;
                if (statusChanged || codeChanged)
                {
                    notification.UpdateDeliveryOutcome(current.Status, current.ErrorCode);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId} ({Error}).",
                    notification.Id, Describe(ex));
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for THIS application's own sending number's messages in the range.
        var providerMessages = await _smsProvider.ListOwnMessagesAsync(from, to, cancellationToken);
        var localRecords = await _notifications.ListAsync(new SentNotificationsInPeriodSpecification(from, to), cancellationToken);

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        var localSids = new HashSet<string>(
            localRecords.Where(n => n.ProviderMessageSid is not null).Select(n => n.ProviderMessageSid!));

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<EShopOnlyRecord>();
        foreach (var n in localRecords)
        {
            if (n.ProviderMessageSid is null) continue;
            if (providerBySid.TryGetValue(n.ProviderMessageSid, out var pm))
            {
                matched.Add(new ReconciliationMatch(n.ProviderMessageSid, pm.Status, pm.ErrorCode, n.Id, n.OrderId, n.Kind.ToString(), n.DeliveryStatus));
            }
            else
            {
                eShopOnly.Add(new EShopOnlyRecord(n.Id, n.OrderId, n.Kind.ToString(), n.ProviderMessageSid, n.DeliveryStatus));
            }
        }

        var providerOnly = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && !localSids.Contains(m.Sid!))
            .Select(m => new ProviderOnlyMessage(m.Sid!, m.Status, m.ErrorCode, m.DateSent))
            .ToList();

        return new ReconciliationReport(
            from, to,
            providerMessages.Count, localRecords.Count, matched.Count,
            matched, providerOnly, eShopOnly);
    }

    private async Task<IReadOnlyList<ContactNumber>> GetNumbersAsync(Order order, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Order {OrderId}: shopper has no number on file; not messaged.", order.Id);
        }
        return numbers;
    }

    private async Task<OrderNotification> SendImmediateAsync(Order order, NotificationKind kind, string toPhoneNumber, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, kind, toPhoneNumber);
        try
        {
            var result = await _smsProvider.SendAsync(toPhoneNumber, body, cancellationToken);
            notification.RecordSendResult(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.RecordSendResult(null, NotificationDeliveryStatus.SendError, null);
            _logger.LogWarning("Order {OrderId}: {Kind} SMS could not be handed to the provider ({Error}); operation still succeeds.",
                order.Id, kind, Describe(ex));
        }
        await _notifications.AddAsync(notification, cancellationToken);
        return notification;
    }

    private static string BuildBody(NotificationKind kind, Order order)
    {
        var total = order.Total().ToString("0.00", CultureInfo.InvariantCulture);
        return kind switch
        {
            NotificationKind.OrderPlaced => $"eShop: Thanks! Your order #{order.Id} has been placed. Order total: ${total}.",
            NotificationKind.OrderDispatched => $"eShop: Good news - your order #{order.Id} is on its way.",
            NotificationKind.DeliveryFollowUp => $"eShop: How did the delivery of your order #{order.Id} go? We'd love your feedback.",
            NotificationKind.OrderCancelled => $"eShop: Your order #{order.Id} has been cancelled. Contact us if this was unexpected.",
            _ => $"eShop: An update on your order #{order.Id}."
        };
    }

    /// <summary>Describes an exception for logs WITHOUT leaking any recipient number or message text.</summary>
    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
