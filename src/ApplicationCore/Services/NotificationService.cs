using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order SMS notifications. Messaging is best-effort: no provider failure is allowed to
/// propagate out of these methods, so the order operation that triggered a notification always
/// succeeds. Shoppers' numbers are never written to logs — only notification/order ids and SIDs are.
/// </summary>
public class NotificationService : INotificationService
{
    /// <summary>How far after dispatch the "how did delivery go?" follow-up is scheduled with the provider.</summary>
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Notification> _notifications;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IReadRepository<Order> _orders;
    private readonly ISmsSender _sms;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<Notification> notifications,
        IReadRepository<ContactNumber> contactNumbers,
        IReadRepository<Order> orders,
        ISmsSender sms,
        IAppLogger<NotificationService> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _orders = orders;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var to = await ResolveNumberAsync(order.BuyerId, cancellationToken);
        if (to is null)
        {
            _logger.LogInformation("Order {OrderId} placed: no contact number on file, not messaging.", order.Id);
            return;
        }

        await SendAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderPlaced, to,
            OrderNotificationMessages.Placed(order), cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var to = await ResolveNumberAsync(order.BuyerId, cancellationToken);
        if (to is null)
        {
            _logger.LogInformation("Order {OrderId} dispatched: no contact number on file, not messaging.", order.Id);
            return;
        }

        // Tell the shopper it's on its way now.
        await SendAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderDispatched, to,
            OrderNotificationMessages.Dispatched(order), cancellationToken);

        // Queue the delivery follow-up with the provider for a few days later. It lives at the provider,
        // not on any timer of ours — we only hold its SID so we can call it off if the order is cancelled.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUp = new Notification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, to,
            OrderNotificationMessages.DeliveryFollowUp(order), sendAt);
        try
        {
            var result = await _sms.ScheduleAsync(to, followUp.Body!, sendAt, cancellationToken);
            followUp.RecordSendResult(result.ProviderSid, result.Status ?? NotificationStatus.NotSent, result.ErrorCode);
            if (!result.Accepted)
                _logger.LogWarning("Order {OrderId} follow-up could not be scheduled: {Reason}", order.Id, result.FailureReason ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} follow-up scheduling threw and was suppressed: {Error}", order.Id, ex.Message);
        }
        await _notifications.AddAsync(followUp, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // Call off any pending follow-up first, so a "how did delivery go?" for a cancelled order can
        // never reach the shopper — independent of whether they still have a number on file.
        var pending = await _notifications.ListAsync(new PendingFollowUpByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pending)
        {
            if (!string.IsNullOrEmpty(followUp.ProviderSid))
            {
                try
                {
                    await _sms.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Order {OrderId} follow-up {NotificationId} cancel threw and was suppressed: {Error}",
                        order.Id, followUp.Id, ex.Message);
                }
            }
            followUp.MarkCanceled();
            await _notifications.UpdateAsync(followUp, cancellationToken);
        }

        var to = await ResolveNumberAsync(order.BuyerId, cancellationToken);
        if (to is null)
        {
            _logger.LogInformation("Order {OrderId} cancelled: no contact number on file, not messaging.", order.Id);
            return;
        }

        await SendAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderCancelled, to,
            OrderNotificationMessages.Cancelled(order), cancellationToken);
    }

    public async Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotent replay: a repeat under the same key returns the notification the first attempt
        // produced, without sending a second message.
        var existing = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Resend key already used; returning notification {NotificationId} without re-sending.", existing.Id);
            return existing;
        }

        var original = await _notifications.FirstOrDefaultAsync(new NotificationByIdSpecification(notificationId), cancellationToken);
        if (original is null)
            return null;

        // Reuse the original text; if it was disposed of, recompose it from the order.
        var body = original.Body ?? await RecomposeBodyAsync(original, cancellationToken);

        var resend = new Notification(original.OrderId, original.BuyerId, original.Type, original.ToPhoneNumber, body);
        resend.MarkResendOf(original.Id, idempotencyKey);
        try
        {
            var result = await _sms.SendAsync(original.ToPhoneNumber, body, cancellationToken);
            resend.RecordSendResult(result.ProviderSid, result.Status ?? NotificationStatus.NotSent, result.ErrorCode);
            if (!result.Accepted)
                _logger.LogWarning("Resend of notification {NotificationId} not accepted: {Reason}", notificationId, result.FailureReason ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} threw and was suppressed: {Error}", notificationId, ex.Message);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<Notification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FirstOrDefaultAsync(new NotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null)
            return null;

        if (!notification.ContentRedacted && !string.IsNullOrEmpty(notification.ProviderSid))
        {
            // Dispose at the provider so the text is unretrievable there too; the record survives.
            await _sms.RedactContentAsync(notification.ProviderSid!, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    public async Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var n in notifications)
        {
            if (string.IsNullOrEmpty(n.ProviderSid) || NotificationStatus.IsTerminal(n.Status))
                continue;

            try
            {
                var result = await _sms.GetStatusAsync(n.ProviderSid!, cancellationToken);
                if (result.Accepted && result.Status is not null)
                {
                    n.UpdateStatus(result.Status, result.ErrorCode);
                    await _notifications.UpdateAsync(n, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Refreshing status of notification {NotificationId} threw and was suppressed: {Error}", n.Id, ex.Message);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var providerRecords = await _sms.ListSentAsync(fromUtc, toUtc, cancellationToken);
        var localSent = await _notifications.ListAsync(new SentNotificationsInRangeSpecification(fromUtc, toUtc), cancellationToken);

        var localBySid = localSent
            .Where(n => n.ProviderSid is not null)
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerRecords.Select(p => p.Sid));

        var matched = new List<ReconciliationMatch>();
        var onlyAtProvider = new List<ProviderMessageRecord>();
        foreach (var p in providerRecords)
        {
            if (localBySid.TryGetValue(p.Sid, out var local))
            {
                matched.Add(new ReconciliationMatch
                {
                    Sid = p.Sid,
                    NotificationId = local.Id,
                    ProviderStatus = p.Status,
                    EShopStatus = local.Status,
                    StatusMatches = string.Equals(p.Status, local.Status, StringComparison.OrdinalIgnoreCase)
                });
            }
            else
            {
                onlyAtProvider.Add(p);
            }
        }

        var onlyInEShop = localBySid.Values.Where(n => !providerSids.Contains(n.ProviderSid!)).ToList();

        return new ReconciliationReport
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            FromNumber = _sms.FromNumber,
            Matched = matched,
            OnlyAtProvider = onlyAtProvider,
            OnlyInEShop = onlyInEShop
        };
    }

    /// <summary>Resolve the shopper's current contact number to message (their most recently registered one).</summary>
    private async Task<string?> ResolveNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.OrderByDescending(c => c.CreatedDate).FirstOrDefault()?.PhoneNumber;
    }

    private async Task SendAndRecordAsync(int orderId, string buyerId, NotificationType type, string to, string body, CancellationToken cancellationToken)
    {
        var notification = new Notification(orderId, buyerId, type, to, body);
        try
        {
            var result = await _sms.SendAsync(to, body, cancellationToken);
            notification.RecordSendResult(result.ProviderSid, result.Status ?? NotificationStatus.NotSent, result.ErrorCode);
            if (result.Accepted)
                _logger.LogInformation("Order {OrderId} {Type} message accepted, sid {Sid}.", orderId, type, result.ProviderSid);
            else
                _logger.LogWarning("Order {OrderId} {Type} message not accepted: {Reason}", orderId, type, result.FailureReason ?? "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} {Type} message threw and was suppressed: {Error}", orderId, type, ex.Message);
        }
        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task<string> RecomposeBodyAsync(Notification original, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(original.OrderId), cancellationToken);
        if (order is null)
            return "eShopOnWeb: An update about your order.";

        return original.Type switch
        {
            NotificationType.OrderPlaced => OrderNotificationMessages.Placed(order),
            NotificationType.OrderDispatched => OrderNotificationMessages.Dispatched(order),
            NotificationType.DeliveryFollowUp => OrderNotificationMessages.DeliveryFollowUp(order),
            NotificationType.OrderCancelled => OrderNotificationMessages.Cancelled(order),
            _ => "eShopOnWeb: An update about your order."
        };
    }
}
