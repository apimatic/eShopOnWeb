using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the messages that go out as an order moves, and the record of what became of each.
/// Sending is best-effort: a message that cannot be sent is recorded as such but never fails the order
/// operation that triggered it. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is queued (within the provider's 15min–35day window).</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IReadRepository<Order> _orders;
    private readonly ISmsProvider _provider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IReadRepository<Order> orders,
        ISmsProvider provider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _provider = provider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToOwnerNumbersAsync(order, NotificationKind.OrderPlaced, cancellationToken);
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return false;
        await NotifyOrderDispatchedAsync(order, cancellationToken);
        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return false;
        await NotifyOrderCancelledAsync(order, cancellationToken);
        return true;
    }

    private async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetOwnerNumbersAsync(order.BuyerId, cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation($"No contact number on file for order {order.Id}; dispatch not messaged.");
            return;
        }

        // Tell them it is on its way now...
        await SendToNumbersAsync(order, NotificationKind.OrderDispatched, numbers, cancellationToken);

        // ...and queue the delivery follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow + FollowUpDelay;
        var body = NotificationMessages.DeliveryFollowUp(order);
        foreach (var number in numbers)
        {
            var followUp = new OrderNotification(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp, number.PhoneNumber, body);
            try
            {
                var result = await _provider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                followUp.RecordProviderResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
                if (result.Created)
                    followUp.MarkScheduled(sendAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Scheduling the delivery follow-up for order {order.Id} failed: {ex.Message}");
                followUp.RecordProviderResult(null, DeliveryStatus.Failed, null, "schedule_error");
            }
            await _notifications.AddAsync(followUp, cancellationToken);
        }
    }

    private async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any follow-up that has not yet gone out — asking how delivery went for a cancelled order
        // is exactly the incident to prevent.
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in followUps)
        {
            try
            {
                var state = await _provider.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState(
                    string.IsNullOrEmpty(state.Status) ? DeliveryStatus.Canceled : state.Status,
                    state.ErrorCode, state.ErrorMessage);
            }
            catch (Exception ex)
            {
                // The order is still cancelled; log loudly because a follow-up that did not cancel could still fire.
                _logger.LogWarning($"Cancelling a scheduled follow-up for order {order.Id} failed: {ex.Message}");
                followUp.UpdateDeliveryState(DeliveryStatus.Canceled, null, "cancel_error");
            }
            await _notifications.UpdateAsync(followUp, cancellationToken);
        }

        // Then tell them the order was cancelled.
        await SendToOwnerNumbersAsync(order, NotificationKind.OrderCancelled, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForOwnerAsync(int orderId, string ownerId, CancellationToken cancellationToken = default)
    {
        // Scope to the caller's own order: return null (→ 404) if it does not exist or is not theirs.
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != ownerId)
            return null;

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        var owned = notifications.Where(n => n.OwnerId == ownerId).ToList();
        await RefreshAsync(owned, cancellationToken);
        return owned;
    }

    public async Task<IReadOnlyList<OwnerOrderSummary>> GetOwnerOrderSummariesAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
        if (orders.Count == 0)
            return new List<OwnerOrderSummary>();

        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId, orderIds), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.OrderBy(n => n.CreatedAt).ToList());
        return orders
            .Select(o => new OwnerOrderSummary(
                o,
                byOrder.TryGetValue(o.Id, out var list) ? list : new List<OrderNotification>()))
            .ToList();
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Repeating the request under the same key returns the first result instead of sending again.
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var prior = await _notifications.FirstOrDefaultAsync(
                new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
            if (prior is not null)
                return new ResendResult(true, prior.Id, prior.Status, Reused: true);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
            return ResendResult.NotFound();

        var body = original.Body;
        if (string.IsNullOrEmpty(body))
        {
            // The original's text may have been disposed of; recompose from the order.
            var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(original.OrderId), cancellationToken);
            body = order is not null ? NotificationMessages.For(original.Kind, order) : NotificationMessages.Generic(original.OrderId);
        }

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Kind, original.ToPhoneNumber, body);
        resend.MarkAsResendOf(original.Id, idempotencyKey);

        // Reserve the idempotency key by persisting the record before sending, then send and update. This keeps a
        // repeat under the same key from producing a second message.
        await _notifications.AddAsync(resend, cancellationToken);
        await TrySendAsync(resend, cancellationToken);
        await _notifications.UpdateAsync(resend, cancellationToken);

        return new ResendResult(true, resend.Id, resend.Status, Reused: false);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return false;

        // Dispose of the text at the provider first; only then clear it locally, so we never claim success
        // while the provider still holds the content.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            await _provider.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation($"Disposed of the content of notification {notificationId}.");
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for the configured sender's messages over the range, then narrow to the exact window.
        var providerMessages = await _provider.ListOutboundFromConfiguredSenderAsync(from, to, cancellationToken);
        var inRange = providerMessages
            .Where(m => m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .ToList();

        var allLocal = await _notifications.ListAsync(new NotificationsWithProviderSidSpecification(), cancellationToken);
        var localBySid = allLocal
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(inRange.Select(m => m.ProviderMessageSid));

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        foreach (var pm in inRange)
        {
            if (localBySid.TryGetValue(pm.ProviderMessageSid, out var local))
            {
                matched.Add(new ReconciliationEntry(pm.ProviderMessageSid, pm.Status, local.Status, local.OrderId, pm.ErrorCode ?? local.ErrorCode));
            }
            else
            {
                // The provider knows about a message eShop has no record of.
                providerOnly.Add(new ReconciliationEntry(pm.ProviderMessageSid, pm.Status, null, null, pm.ErrorCode));
            }
        }

        var eShopOnly = new List<ReconciliationEntry>();
        foreach (var local in allLocal)
        {
            if (local.ProviderMessageSid is null || providerSids.Contains(local.ProviderMessageSid))
                continue;
            if (!BelievedSentInRange(local, from, to))
                continue;
            // eShop believes it sent a message the provider's record for the range does not show.
            eShopOnly.Add(new ReconciliationEntry(local.ProviderMessageSid, null, local.Status, local.OrderId, local.ErrorCode));
        }

        return new ReconciliationReport(
            from, to, _provider.ConfiguredSenderNumber,
            ProviderCount: inRange.Count,
            EShopCount: matched.Count + eShopOnly.Count,
            MatchedCount: matched.Count,
            Matched: matched,
            ProviderOnly: providerOnly,
            EShopOnly: eShopOnly);
    }

    private static bool BelievedSentInRange(OrderNotification local, DateTimeOffset from, DateTimeOffset to)
    {
        // A message eShop considers actually sent (not merely scheduled or never attempted) within the window.
        if (local.CreatedAt < from || local.CreatedAt > to)
            return false;
        return local.Status is not (DeliveryStatus.Scheduled or DeliveryStatus.NotSent);
    }

    // --- helpers ---

    private async Task<IReadOnlyList<ContactNumber>> GetOwnerNumbersAsync(string ownerId, CancellationToken cancellationToken)
        => await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);

    private async Task SendToOwnerNumbersAsync(Order order, NotificationKind kind, CancellationToken cancellationToken)
    {
        var numbers = await GetOwnerNumbersAsync(order.BuyerId, cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation($"No contact number on file for order {order.Id}; {kind} not messaged.");
            return;
        }
        await SendToNumbersAsync(order, kind, numbers, cancellationToken);
    }

    private async Task SendToNumbersAsync(Order order, NotificationKind kind, IReadOnlyList<ContactNumber> numbers, CancellationToken cancellationToken)
    {
        var body = NotificationMessages.For(kind, order);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, kind, number.PhoneNumber, body);
            await _notifications.AddAsync(notification, cancellationToken);
            await TrySendAsync(notification, cancellationToken);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task TrySendAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _provider.SendAsync(notification.ToPhoneNumber, notification.Body!, cancellationToken);
            notification.RecordProviderResult(result.ProviderMessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning($"Sending a {notification.Kind} message for order {notification.OrderId} failed: {ex.Message}");
            notification.RecordProviderResult(null, DeliveryStatus.Failed, null, "send_error");
        }
    }

    private async Task RefreshAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || notification.IsTerminal())
                continue;
            try
            {
                var state = await _provider.FetchAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Reading the latest outcome is best-effort; keep the last known state on a provider hiccup.
                _logger.LogWarning($"Refreshing delivery status for notification {notification.Id} failed: {ex.Message}");
            }
        }
    }
}
