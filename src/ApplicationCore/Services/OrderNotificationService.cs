using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Sends the SMS notifications that accompany an order's lifecycle and services the operator actions
/// on them. All notify paths are best-effort with respect to the underlying order operation: a
/// message that cannot be sent is recorded as failed and never thrown back to the caller.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsGateway _smsGateway;
    private readonly INotificationOptions _options;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ISmsGateway smsGateway,
        INotificationOptions options,
        IAppLogger<OrderNotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _smsGateway = smsGateway;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendImmediateAsync(order, NotificationType.OrderPlaced, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);

        if (numbers.Count == 0)
        {
            await RecordNoContactNumberAsync(order, NotificationType.OrderDispatched, cancellationToken);
            return;
        }

        // Tell them it is on its way now...
        foreach (var number in numbers)
        {
            await SendOneAsync(order, NotificationType.OrderDispatched, number.PhoneNumber, cancellationToken);
        }

        // ...and queue the follow-up with the provider for a few days later.
        var sendAt = DateTimeOffset.UtcNow.Add(_options.DeliveryFollowUpDelay);
        foreach (var number in numbers)
        {
            await ScheduleFollowUpAsync(order, number.PhoneNumber, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Critical: a follow-up that has not yet gone out must never reach the shopper. Call off any
        // scheduled follow-up for this order first.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await SendImmediateAsync(order, NotificationType.OrderCancelled, cancellationToken);
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the notification produced the first time.
        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Resend replayed for idempotency key; returning notification {NotificationId}.", existing.Id);
            return new ResendResult(existing, Replayed: true);
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.Type, original.ToPhoneNumber);
        resend.MarkAsResend(original.Id, idempotencyKey);

        // Persist the key before contacting the provider so a concurrent replay is deduplicated.
        resend = await _notifications.AddAsync(resend, cancellationToken);

        if (string.IsNullOrEmpty(original.ToPhoneNumber))
        {
            resend.RecordNoContactNumber();
            await _notifications.UpdateAsync(resend, cancellationToken);
            return new ResendResult(resend, Replayed: false);
        }

        await DispatchImmediateAsync(resend, ComposeBody(original.Type, original.OrderId), cancellationToken);
        await _notifications.UpdateAsync(resend, cancellationToken);
        return new ResendResult(resend, Replayed: false);
    }

    public async Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        // If the message reached the provider, dispose of its text there. The fact of the message and
        // its delivery outcome survive. Failure here is NOT swallowed — we must not claim disposal we
        // did not achieve.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var state = await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage, state.DateSent);
        }

        notification.MarkContentRedacted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Redacted content of notification {NotificationId}.", notification.Id);
        return notification;
    }

    public async Task RefreshDeliveryStatesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        // The notifications passed in are already tracked by the shared context (they came from a
        // repository query). Mutate those tracked instances in place and persist once at the end, rather
        // than re-attaching each via UpdateAsync (which would conflict with the already-tracked instance).
        var changed = false;

        foreach (var notification in notifications)
        {
            if (notification.IsTerminal() || string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.GetAsync(notification.ProviderMessageSid!, cancellationToken);
                if (state is null)
                {
                    continue;
                }

                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage, state.DateSent);
                changed = true;
            }
            catch (Exception ex)
            {
                // A provider hiccup must never fail a read; keep the last-known state.
                _logger.LogWarning("Could not refresh delivery state for notification {NotificationId}: {Error}",
                    notification.Id, ex.Message);
            }
        }

        if (changed)
        {
            try
            {
                await _notifications.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not persist refreshed delivery states: {Error}", ex.Message);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListSentFromAsync(from, to, cancellationToken);

        // What this application believes it sent from the configured number in the window: any
        // notification that carries a provider SID and was created within the range.
        var allNotifications = await _notifications.ListAsync(cancellationToken);
        var eShopSent = allNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid) && n.CreatedAt >= from && n.CreatedAt <= to)
            .ToList();

        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());
        var eShopBySid = eShopSent
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, message) in providerBySid)
        {
            if (eShopBySid.TryGetValue(sid, out var notification))
            {
                matched.Add(BuildEntry(sid, message, notification));
            }
            else
            {
                providerOnly.Add(BuildEntry(sid, message, null));
            }
        }

        foreach (var (sid, notification) in eShopBySid)
        {
            if (!providerBySid.ContainsKey(sid))
            {
                eShopOnly.Add(BuildEntry(sid, null, notification));
            }
        }

        return new ReconciliationReport(
            from, to, _smsGateway.SendingNumber,
            providerBySid.Count, eShopBySid.Count,
            matched.OrderBy(e => e.ProviderDateSent).ToList(),
            providerOnly.OrderBy(e => e.ProviderDateSent).ToList(),
            eShopOnly.OrderBy(e => e.NotificationId).ToList());
    }

    private static ReconciliationEntry BuildEntry(string sid, SmsMessageState? message, OrderNotification? notification)
    {
        return new ReconciliationEntry(
            sid,
            message?.Status ?? notification?.ProviderStatusRaw,
            message?.DateSent ?? notification?.ProviderDateSent,
            notification?.Id,
            notification?.Type,
            notification?.OrderId,
            notification?.Status);
    }

    // ----- internals -----

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken ct)
    {
        return await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
    }

    private async Task SendImmediateAsync(Order order, NotificationType type, CancellationToken ct)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, ct);
        if (numbers.Count == 0)
        {
            await RecordNoContactNumberAsync(order, type, ct);
            return;
        }

        foreach (var number in numbers)
        {
            await SendOneAsync(order, type, number.PhoneNumber, ct);
        }
    }

    private async Task SendOneAsync(Order order, NotificationType type, string toPhoneNumber, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toPhoneNumber);
        notification = await _notifications.AddAsync(notification, ct);
        await DispatchImmediateAsync(notification, ComposeBody(type, order.Id), ct);
        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task DispatchImmediateAsync(OrderNotification notification, string body, CancellationToken ct)
    {
        try
        {
            var state = await _smsGateway.SendAsync(notification.ToPhoneNumber!, body, ct);
            notification.RecordAccepted(state.Sid, state.Status, scheduled: false, scheduledFor: null, providerDateSent: state.DateSent);
            _logger.LogInformation("Sent {Type} notification {NotificationId} for order {OrderId} (sid {Sid}, status {Status}).",
                notification.Type, notification.Id, notification.OrderId, state.Sid, state.Status ?? "queued");
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.RecordSendFailed(ex.Message);
            _logger.LogWarning("Could not send {Type} notification {NotificationId} for order {OrderId}: {Error}",
                notification.Type, notification.Id, notification.OrderId, ex.Message);
        }
    }

    private async Task ScheduleFollowUpAsync(Order order, string toPhoneNumber, DateTimeOffset sendAt, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, toPhoneNumber);
        notification = await _notifications.AddAsync(notification, ct);

        try
        {
            var state = await _smsGateway.ScheduleAsync(toPhoneNumber, ComposeBody(NotificationType.DeliveryFollowUp, order.Id), sendAt, ct);
            notification.RecordAccepted(state.Sid, state.Status, scheduled: true, scheduledFor: sendAt, providerDateSent: null);
            _logger.LogInformation("Scheduled delivery follow-up {NotificationId} for order {OrderId} (sid {Sid}).",
                notification.Id, order.Id, state.Sid);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailed(ex.Message);
            _logger.LogWarning("Could not schedule delivery follow-up {NotificationId} for order {OrderId}: {Error}",
                notification.Id, order.Id, ex.Message);
        }

        await _notifications.UpdateAsync(notification, ct);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);

        // Cancel EVERY follow-up that has been handed to the provider but has not yet gone out. We must
        // not narrow this to a single status: a freshly scheduled SMS can still read "queued" for a
        // moment, and letting such a follow-up slip through uncancelled is exactly the incident to
        // prevent. Anything already delivered/failed/canceled is left alone (the provider would reject
        // cancelling it anyway, and that is handled below).
        var pendingFollowUps = notifications.Where(n =>
            n.Type == NotificationType.DeliveryFollowUp &&
            !string.IsNullOrEmpty(n.ProviderMessageSid) &&
            n.Status is not (NotificationStatus.Canceled or NotificationStatus.Sent
                or NotificationStatus.Delivered or NotificationStatus.Undelivered
                or NotificationStatus.Failed or NotificationStatus.SendFailed));

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                await _smsGateway.CancelAsync(followUp.ProviderMessageSid!, ct);
                followUp.MarkCanceled();
                await _notifications.UpdateAsync(followUp, ct);
                _logger.LogInformation("Cancelled scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task RecordNoContactNumberAsync(Order order, NotificationType type, CancellationToken ct)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toPhoneNumber: null);
        notification.RecordNoContactNumber();
        await _notifications.AddAsync(notification, ct);
        _logger.LogInformation("No contact number on file for order {OrderId}; {Type} not sent.", order.Id, type);
    }

    private static string ComposeBody(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced => $"eShopOnWeb: Thanks! Your order #{orderId} has been placed. We'll text you as it moves.",
        NotificationType.OrderDispatched => $"eShopOnWeb: Good news - your order #{orderId} is on its way!",
        NotificationType.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of your order #{orderId} go? Reply to let us know.",
        NotificationType.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled. Contact support if this is unexpected.",
        _ => $"eShopOnWeb: Update on your order #{orderId}."
    };
}
