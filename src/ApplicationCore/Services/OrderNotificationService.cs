using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Drives the SMS notifications that accompany an order's lifecycle. Sending is best-effort: a message that
/// cannot go out is recorded and the underlying order operation still succeeds. Phone numbers are treated as
/// sensitive and never appear in a log line.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IReadRepository<Order> _orders;
    private readonly ISmsGateway _gateway;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly OrderNotificationOptions _options;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IReadRepository<Order> orders,
        ISmsGateway gateway,
        IAppLogger<OrderNotificationService> logger,
        OrderNotificationOptions options)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _orders = orders;
        _gateway = gateway;
        _logger = logger;
        _options = options;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct)
    {
        try
        {
            foreach (var number in await NumbersForAsync(order.BuyerId, ct))
                await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderPlaced, order.Total(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {0}: order-placed notifications failed. {1}", order.Id, Sanitize(ex));
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct)
    {
        try
        {
            foreach (var number in await NumbersForAsync(order.BuyerId, ct))
            {
                await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderDispatched, null, ct);
                await ScheduleFollowUpAsync(order, number.PhoneNumber, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {0}: order-dispatched notifications failed. {1}", order.Id, Sanitize(ex));
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct)
    {
        try
        {
            // The critical safety step first: call off any follow-up that has not yet gone out, so a shopper
            // is never asked how a delivery went for an order that was cancelled.
            var scheduled = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(order.Id), ct);
            foreach (var followUp in scheduled)
            {
                try
                {
                    await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                    followUp.MarkCanceled();
                    await _notifications.UpdateAsync(followUp, ct);
                }
                catch (Exception ex)
                {
                    // Leave it as scheduled so reconciliation surfaces it; do not claim it was called off.
                    _logger.LogWarning("Order {0}: could not call off scheduled follow-up notification {1}. {2}",
                        order.Id, followUp.Id, Sanitize(ex));
                }
            }

            foreach (var number in await NumbersForAsync(order.BuyerId, ct))
                await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderCancelled, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {0}: order-cancelled notifications failed. {1}", order.Id, Sanitize(ex));
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);

        foreach (var n in notifications)
        {
            if (n.ProviderMessageSid == null || n.Status.IsTerminal())
                continue;

            try
            {
                var state = await _gateway.FetchStatusAsync(n.ProviderMessageSid, ct);
                var mapped = NotificationStatusMapper.FromProviderStatus(state.ProviderStatus);
                if (mapped != NotificationStatus.Unknown)
                {
                    n.UpdateDeliveryState(mapped, state.ProviderStatus, state.ErrorCode, state.ErrorMessage);
                    await _notifications.UpdateAsync(n, ct);
                }
            }
            catch (SmsGatewayException ex)
            {
                // Degrade to the last-known outcome rather than failing the read.
                _logger.LogWarning("Order {0}: could not refresh notification {1}. {2}", orderId, n.Id, ex.Message);
            }
        }

        return notifications;
    }

    public async Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a request already handled under this key returns the earlier result and sends nothing.
        var already = await _notifications.FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (already != null)
            return new ResendOutcome(already.Id, AlreadyProcessed: true);

        var source = await _notifications.GetByIdAsync(notificationId, ct);
        if (source == null)
            return null;

        var body = source.Body;
        if (string.IsNullOrEmpty(body))
        {
            // The content was disposed of; rebuild a faithful message from the order so the re-send still means something.
            var order = await _orders.GetByIdAsync(source.OrderId, ct);
            var total = source.Type == NotificationType.OrderPlaced ? order?.Total() : null;
            body = ComposeBody(source.Type, source.OrderId, total);
        }

        var resend = OrderNotification.ForResend(source, idempotencyKey, body!);
        try
        {
            var result = await _gateway.SendAsync(source.ToPhoneNumber, body!, ct);
            resend.MarkSubmitted(result.Sid, NotificationStatusMapper.FromProviderStatus(result.ProviderStatus),
                result.ProviderStatus, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            resend.MarkSubmitFailed(Sanitize(ex));
            _logger.LogWarning("Re-send of notification {0} could not be sent. {1}", notificationId, Sanitize(ex));
        }

        await _notifications.AddAsync(resend, ct);
        return new ResendOutcome(resend.Id, AlreadyProcessed: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct)
    {
        var n = await _notifications.GetByIdAsync(notificationId, ct);
        if (n == null)
            return false;

        // Redact at the provider so the text is no longer retrievable there either. If this fails we do NOT
        // claim success — the whole point is that the content is gone at the provider, so the failure surfaces.
        if (n.ProviderMessageSid != null)
            await _gateway.RedactContentAsync(n.ProviderMessageSid, ct);

        n.MarkContentDisposed();
        await _notifications.UpdateAsync(n, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // Ask the provider only for our own sending number's messages in the range (filter applied provider-side).
        var providerMessages = await _gateway.ListSentFromConfiguredNumberAsync(from, to, ct);
        var localNotifications = await _notifications.ListAsync(
            new OrderNotificationsWithProviderMessageInRangeSpecification(from, to), ct);

        // What eShop believes it actually sent from its number: carries a SID and is neither merely scheduled
        // nor called off.
        var believedSent = localNotifications
            .Where(n => n.ProviderMessageSid != null
                && n.Status != NotificationStatus.Scheduled
                && n.Status != NotificationStatus.Canceled)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var (sid, providerMsg) in providerBySid)
        {
            if (believedSent.TryGetValue(sid, out var local))
                matched.Add(new ReconciliationEntry(sid, local.Id, local.OrderId, providerMsg.ProviderStatus, local.Status.ToString()));
            else
                providerOnly.Add(new ReconciliationEntry(sid, null, null, providerMsg.ProviderStatus, null));
        }

        foreach (var (sid, local) in believedSent)
        {
            if (!providerBySid.ContainsKey(sid))
                eShopOnly.Add(new ReconciliationEntry(sid, local.Id, local.OrderId, null, local.Status.ToString()));
        }

        return new ReconciliationReport(from, to, providerMessages.Count, believedSent.Count,
            matched.Count, matched, providerOnly, eShopOnly);
    }

    // --- helpers ---

    private async Task<IReadOnlyList<ContactNumber>> NumbersForAsync(string buyerId, CancellationToken ct)
        => await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

    private async Task<OrderNotification> SendImmediateAsync(
        Order order, string toNumber, NotificationType type, decimal? total, CancellationToken ct)
    {
        var body = ComposeBody(type, order.Id, total);
        var notification = OrderNotification.ForEvent(order.Id, order.BuyerId, toNumber, type, body);

        try
        {
            var result = await _gateway.SendAsync(toNumber, body, ct);
            notification.MarkSubmitted(result.Sid, NotificationStatusMapper.FromProviderStatus(result.ProviderStatus),
                result.ProviderStatus, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSubmitFailed(Sanitize(ex));
            _logger.LogWarning("Order {0}: {1} notification could not be sent. {2}", order.Id, type, Sanitize(ex));
        }

        await _notifications.AddAsync(notification, ct);
        return notification;
    }

    private async Task ScheduleFollowUpAsync(Order order, string toNumber, CancellationToken ct)
    {
        var body = ComposeBody(NotificationType.DeliveryFollowUp, order.Id, null);
        var notification = OrderNotification.ForEvent(order.Id, order.BuyerId, toNumber, NotificationType.DeliveryFollowUp, body);

        try
        {
            var sendAt = DateTimeOffset.UtcNow.AddHours(_options.FollowUpDelayHours);
            var result = await _gateway.ScheduleAsync(toNumber, body, sendAt, ct);
            notification.MarkSubmitted(result.Sid, NotificationStatusMapper.FromProviderStatus(result.ProviderStatus),
                result.ProviderStatus, result.ErrorCode, result.ErrorMessage, scheduledSendAt: sendAt);
        }
        catch (Exception ex)
        {
            notification.MarkSubmitFailed(Sanitize(ex));
            _logger.LogWarning("Order {0}: delivery follow-up could not be scheduled. {1}", order.Id, Sanitize(ex));
        }

        await _notifications.AddAsync(notification, ct);
    }

    private static string ComposeBody(NotificationType type, int orderId, decimal? total)
    {
        return type switch
        {
            NotificationType.OrderPlaced => total.HasValue
                ? $"eShop: your order #{orderId} has been placed (total {total.Value.ToString("C", CultureInfo.GetCultureInfo("en-US"))}). Thanks for shopping with us!"
                : $"eShop: your order #{orderId} has been placed. Thanks for shopping with us!",
            NotificationType.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
            NotificationType.DeliveryFollowUp => $"eShop: how did the delivery of your order #{orderId} go? We'd love your feedback.",
            NotificationType.OrderCancelled => $"eShop: your order #{orderId} has been cancelled. Please contact support if this is unexpected.",
            _ => $"eShop: an update about your order #{orderId}."
        };
    }

    /// <summary>Produce a caller-safe description of a failure — never a phone number, never raw SDK detail.</summary>
    private static string Sanitize(Exception ex)
        => ex is SmsGatewayException ? ex.Message : "An unexpected error occurred while contacting the SMS provider.";
}
