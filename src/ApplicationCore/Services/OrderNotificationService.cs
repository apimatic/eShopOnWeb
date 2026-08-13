using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves, and the operator actions
/// on them. Sending is always best-effort: a message that cannot go out is recorded as such
/// and never aborts the order operation that triggered it.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<Notification> _notifications;
    private readonly ISmsGateway _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    // How far ahead the "how did delivery go?" follow-up is queued with the provider.
    // Well within the provider's scheduling window (a quarter hour to seven days out).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<Notification> notifications,
        ISmsGateway sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var number = await GetActiveNumberAsync(order.BuyerId, cancellationToken);
        var body = $"eShop: your order #{order.Id} has been placed. " +
                   $"Order total {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. " +
                   "Thanks for shopping with us!";
        await SendImmediateAsync(order.Id, order.BuyerId, NotificationType.OrderPlaced, number, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var number = await GetActiveNumberAsync(order.BuyerId, cancellationToken);

        var dispatchBody = $"eShop: good news! Your order #{order.Id} has been dispatched and is on its way.";
        await SendImmediateAsync(order.Id, order.BuyerId, NotificationType.OrderDispatched, number, dispatchBody, cancellationToken);

        // Queue the delivery follow-up with the provider for a few days later. The wait is held
        // by the provider, not by a timer inside this application.
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? " +
                           "We'd love your feedback - just reply to this message.";
        await ScheduleFollowUpAsync(order.Id, order.BuyerId, number, followUpBody,
            DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var number = await GetActiveNumberAsync(order.BuyerId, cancellationToken);

        var cancelBody = $"eShop: your order #{order.Id} has been cancelled. " +
                         "If you didn't expect this, please contact support.";
        await SendImmediateAsync(order.Id, order.BuyerId, NotificationType.OrderCancelled, number, cancelBody, cancellationToken);

        // Call off any delivery follow-up that has not yet gone out — asking how delivery went
        // for a cancelled order is exactly the incident this prevents.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderSid is null || NotificationStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _sms.FetchAsync(notification.ProviderSid, cancellationToken);
                if (state is not null)
                {
                    notification.UpdateDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception)
            {
                // Refreshing delivery state is best-effort; keep the last known status on failure.
            }
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // A repeat under the same key must not send a second message.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var prior = await _notifications.FirstOrDefaultAsync(
                new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
            if (prior is not null)
            {
                return new ResendResult(true, prior.Id, Duplicate: true, Error: null);
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult(false, null, false, "Notification not found.");
        }

        if (string.IsNullOrEmpty(original.ToNumber))
        {
            return new ResendResult(false, null, false, "The original message has no destination on record.");
        }

        if (original.ContentDeleted || string.IsNullOrEmpty(original.Body))
        {
            return new ResendResult(false, null, false, "The message content is no longer available to resend.");
        }

        var resend = new Notification(original.OrderId, original.BuyerId, NotificationType.Resend, original.ToNumber, original.Body);
        resend.SetIdempotencyKey(idempotencyKey);

        // Persist first so the idempotency key is on record before the send completes and a
        // concurrent repeat can find it.
        resend = await _notifications.AddAsync(resend, cancellationToken);

        try
        {
            var result = await _sms.SendAsync(original.ToNumber, original.Body, cancellationToken);
            resend.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, false);
        }
        catch (SmsGatewayException ex)
        {
            resend.MarkSendFailed(ex.ErrorCode, ex.Message);
            LogSendFailure(NotificationType.Resend, original.OrderId, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed(null, ex.Message);
            LogSendFailure(NotificationType.Resend, original.OrderId, null);
        }

        await _notifications.UpdateAsync(resend, cancellationToken);
        return new ResendResult(true, resend.Id, false, null);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // Redact at the provider first, so reporting success means the text really is gone there.
        // The fact that a message was sent, and what became of it, survives on the record below.
        if (notification.ProviderSid is not null && !notification.ContentDeleted)
        {
            await _sms.RedactBodyAsync(notification.ProviderSid, cancellationToken);
        }

        notification.MarkContentDeleted();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var fromNumber = _sms.SenderNumber;

        // Ask the provider for this number's messages over the range (server-side sender filter),
        // rather than pulling a wider answer and filtering after the fact.
        var providerMessages = await _sms.ListSentFromAsync(fromNumber, fromUtc, toUtc, cancellationToken);

        var localNotifications = await _notifications.ListAsync(
            new NotificationsWithProviderSidInRangeSpecification(fromUtc, toUtc), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderSid is not null)
            .GroupBy(n => n.ProviderSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var providerBySid = providerMessages
            .GroupBy(m => m.Sid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var localOnly = new List<ReconciliationEntry>();

        foreach (var message in providerBySid.Values)
        {
            if (localBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(
                    message.Sid, notification.Id, notification.OrderId,
                    notification.Status, message.Status, message.DateSent));
            }
            else
            {
                // The provider knows about this message; eShop does not.
                providerOnly.Add(new ReconciliationEntry(
                    message.Sid, null, null, null, message.Status, message.DateSent));
            }
        }

        foreach (var notification in localBySid.Values)
        {
            if (!providerBySid.ContainsKey(notification.ProviderSid!))
            {
                // eShop believes it sent this; the provider's record for the range does not show it.
                localOnly.Add(new ReconciliationEntry(
                    notification.ProviderSid!, notification.Id, notification.OrderId,
                    notification.Status, null, null));
            }
        }

        return new ReconciliationReport(
            fromUtc, toUtc, fromNumber,
            providerBySid.Count, localBySid.Count,
            matched, providerOnly, localOnly);
    }

    private async Task<ContactNumber?> GetActiveNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        // A shopper may have more than one number on file; use the most recently registered one.
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }

    private async Task<Notification> SendImmediateAsync(
        int orderId, string buyerId, NotificationType type, ContactNumber? number, string body, CancellationToken cancellationToken)
    {
        var notification = new Notification(orderId, buyerId, type, number?.PhoneNumber ?? string.Empty, body);

        // A shopper with no number on file is simply not messaged.
        if (number is null)
        {
            notification.MarkNoContactNumber();
            return await _notifications.AddAsync(notification, cancellationToken);
        }

        try
        {
            var result = await _sms.SendAsync(number.PhoneNumber, body, cancellationToken);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, false);
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkSendFailed(ex.ErrorCode, ex.Message);
            LogSendFailure(type, orderId, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(null, ex.Message);
            LogSendFailure(type, orderId, null);
        }

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(
        int orderId, string buyerId, ContactNumber? number, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new Notification(orderId, buyerId, NotificationType.DeliveryFollowUp, number?.PhoneNumber ?? string.Empty, body);

        if (number is null)
        {
            notification.MarkNoContactNumber();
            await _notifications.AddAsync(notification, cancellationToken);
            return;
        }

        try
        {
            var result = await _sms.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
            notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, isScheduled: true);
        }
        catch (SmsGatewayException ex)
        {
            notification.MarkSendFailed(ex.ErrorCode, ex.Message);
            LogSendFailure(NotificationType.DeliveryFollowUp, orderId, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed(null, ex.Message);
            LogSendFailure(NotificationType.DeliveryFollowUp, orderId, null);
        }

        await _notifications.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);

        var pendingFollowUps = notifications.Where(n =>
            n.Type == NotificationType.DeliveryFollowUp &&
            n.ProviderSid is not null &&
            !NotificationStatus.IsTerminal(n.Status));

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                await _sms.CancelScheduledAsync(followUp.ProviderSid!, cancellationToken);
                followUp.UpdateDeliveryState(NotificationStatus.Canceled, null, null);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                // Cancelling the order must still succeed; the follow-up's record stays visible
                // to the operator, who can act on it via the notification endpoints.
                _logger.LogWarning($"Could not call off scheduled follow-up for order #{orderId}.");
            }
        }
    }

    private void LogSendFailure(NotificationType type, int orderId, int? errorCode)
    {
        // Never log the destination number or message body.
        var code = errorCode.HasValue ? errorCode.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
        _logger.LogWarning($"A {type} message for order #{orderId} could not be sent (provider error code {code}).");
    }
}
