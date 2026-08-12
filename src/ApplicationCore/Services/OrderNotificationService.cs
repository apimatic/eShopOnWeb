using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order notifications and the operator actions on top of them. Depends only on
/// repositories and the messaging abstraction, so it carries no provider details.
/// Phone numbers and message bodies are never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far out the "how did delivery go?" follow-up is queued with the provider.</summary>
    private const int FollowUpDelayDays = 3;

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsMessagingService _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ISmsMessagingService sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetNumbersAsync(order.BuyerId, order.Id, cancellationToken);
        var body = $"eShop: your order #{order.Id} has been placed (total {FormatTotal(order)}). Thank you for shopping with us!";
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order.Id, order.BuyerId, NotificationKind.OrderPlaced, number.PhoneNumber, body, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetNumbersAsync(order.BuyerId, order.Id, cancellationToken);
        var dispatchBody = $"eShop: good news — your order #{order.Id} is on its way!";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.AddDays(FollowUpDelayDays);

        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched, number.PhoneNumber, dispatchBody, cancellationToken);
            await ScheduleAndRecordAsync(order.Id, order.BuyerId, number.PhoneNumber, followUpBody, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // First: call off any not-yet-sent delivery follow-up so it can never reach the shopper.
        var existing = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in existing.Where(n => n.Kind == NotificationKind.DeliveryFollowUp && n.CanBeCancelled()))
        {
            try
            {
                var result = await _sms.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateDeliveryState(
                    string.IsNullOrEmpty(result.Status) ? DeliveryStatuses.Canceled : result.Status,
                    result.ErrorCode,
                    null);
                if (!followUp.DeliveryStatus.Equals(DeliveryStatuses.Canceled, StringComparison.OrdinalIgnoreCase))
                {
                    followUp.MarkCanceled();
                }
                await _notifications.UpdateAsync(followUp, cancellationToken);
                _logger.LogInformation("Called off scheduled delivery follow-up (notification {0}) for order {1}.", followUp.Id, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up (notification {0}) for order {1}: {2}", followUp.Id, order.Id, ex.GetType().Name);
            }
        }

        // Then: tell the shopper the order was cancelled.
        var numbers = await GetNumbersAsync(order.BuyerId, order.Id, cancellationToken);
        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled, number.PhoneNumber, body, cancellationToken);
        }
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeated request under the same key returns the already-produced
        // notification and does not send a second message.
        var alreadyDone = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (alreadyDone is not null)
        {
            _logger.LogInformation("Resend idempotency key already seen; returning notification {0} without sending again.", alreadyDone.Id);
            return alreadyDone;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        var to = original.ToPhoneNumber;
        var body = original.MessageBody ?? $"eShop: an update about your order #{original.OrderId}.";

        // Reserve the idempotency key by persisting the resend record before sending, so a
        // concurrent duplicate finds it and does not send again.
        var resend = new OrderNotification(original.OrderId, original.OwnerId, NotificationKind.Resend, to, body);
        resend.SetIdempotencyKey(idempotencyKey);
        resend.SetResendOf(original.Id);
        await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _sms.SendAsync(to, body, cancellationToken);
            resend.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.DateSent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {0} (as {1}) could not be submitted: {2}", original.Id, resend.Id, ex.GetType().Name);
            resend.MarkSendFailed(ex.GetType().Name);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return resend;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Dispose of the content at the provider first so its text is no longer retrievable there.
        // Only then clear it locally, keeping the record that a message was sent and what became of it.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _sms.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {0} (order {1}).", notification.Id, notification.OrderId);
        return true;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOwnerAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _sms.ListSentMessagesAsync(fromUtc, toUtc, cancellationToken);
        var eShopNotifications = await _notifications.ListAsync(new OrderNotificationsSentBetweenSpecification(fromUtc, toUtc), cancellationToken);

        var eShopBySid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        int matched = 0, providerOnly = 0, eShopOnly = 0;

        foreach (var message in providerMessages)
        {
            if (eShopBySid.TryGetValue(message.Sid, out var known))
            {
                matched++;
                entries.Add(new ReconciliationEntry(
                    message.Sid, "matched", message.Status, message.To, message.DateSent,
                    known.Id, known.OrderId, known.Kind.ToString(), known.DeliveryStatus));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(
                    message.Sid, "provider_only", message.Status, message.To, message.DateSent,
                    null, null, null, null));
            }
        }

        foreach (var notification in eShopNotifications.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            if (!providerSids.Contains(notification.ProviderMessageSid!))
            {
                eShopOnly++;
                entries.Add(new ReconciliationEntry(
                    notification.ProviderMessageSid, "eshop_only", null, null, null,
                    notification.Id, notification.OrderId, notification.Kind.ToString(), notification.DeliveryStatus));
            }
        }

        return new ReconciliationReport(
            fromUtc, toUtc, _sms.FromNumber,
            providerMessages.Count, eShopNotifications.Count,
            matched, providerOnly, eShopOnly, entries);
    }

    private async Task<IReadOnlyList<ContactNumber>> GetNumbersAsync(string ownerId, int orderId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Order {0} has no contact number on file for its shopper; not messaging.", orderId);
        }
        return numbers;
    }

    private async Task<OrderNotification> SendAndRecordAsync(
        int orderId, string ownerId, NotificationKind kind, string to, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, ownerId, kind, to, body);
        try
        {
            var result = await _sms.SendAsync(to, body, cancellationToken);
            notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.DateSent);
            _logger.LogInformation("Sent {0} message for order {1} (notification recorded, status {2}).", kind, orderId, notification.DeliveryStatus);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Could not send {0} message for order {1}: {2}", kind, orderId, ex.GetType().Name);
            notification.MarkSendFailed(ex.GetType().Name);
        }

        await _notifications.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task<OrderNotification> ScheduleAndRecordAsync(
        int orderId, string ownerId, string to, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(orderId, ownerId, NotificationKind.DeliveryFollowUp, to, body);
        notification.MarkScheduled(sendAtUtc);
        try
        {
            var result = await _sms.ScheduleAsync(to, body, sendAtUtc, cancellationToken);
            notification.ApplyProviderResult(result.Sid, result.Status, result.ErrorCode, result.DateSent);
            _logger.LogInformation("Queued delivery follow-up for order {0} with the provider (status {1}).", orderId, notification.DeliveryStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not queue delivery follow-up for order {0}: {1}", orderId, ex.GetType().Name);
            notification.MarkSendFailed(ex.GetType().Name);
        }

        await _notifications.AddAsync(notification, cancellationToken);
        return notification;
    }

    private async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid) || DeliveryStatuses.IsFinal(notification.DeliveryStatus))
            {
                continue;
            }

            try
            {
                var state = await _sms.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.DateSent);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Reporting must not fail because a status read failed; keep the last known outcome.
                _logger.LogWarning("Could not refresh delivery status for notification {0}: {1}", notification.Id, ex.GetType().Name);
            }
        }
    }

    private static string FormatTotal(Order order)
        => order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
