using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the SMS a shopper receives as an order moves, and the operator recovery actions.
///
/// Two invariants run through every method here:
/// <list type="bullet">
/// <item>A message that cannot be sent never fails the underlying order operation — every provider
/// call is guarded and its failure is recorded as an outcome, not raised.</item>
/// <item>A shopper's phone number is never written to a log — only order ids, notification ids,
/// statuses and SIDs are.</item>
/// </list>
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far after dispatch the "how did delivery go?" follow-up is queued for.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly ISmsProvider _sms;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<SmsNotification> notifications,
        ISmsProvider sms,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _sms = sms;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us.";
        foreach (var number in await GetOwnerNumbersAsync(order.BuyerId))
        {
            await SendNowAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderPlaced, number.PhoneNumber, body, null, cancellationToken);
        }
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchBody = $"eShop: good news - your order #{order.Id} is on its way.";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We would love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in await GetOwnerNumbersAsync(order.BuyerId))
        {
            await SendNowAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderDispatched, number.PhoneNumber, dispatchBody, null, cancellationToken);
            await ScheduleAndRecordAsync(order.Id, order.BuyerId, number.PhoneNumber, followUpBody, sendAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact us with any questions.";
        foreach (var number in await GetOwnerNumbersAsync(order.BuyerId))
        {
            await SendNowAndRecordAsync(order.Id, order.BuyerId, NotificationType.OrderCancelled, number.PhoneNumber, body, null, cancellationToken);
        }

        // A follow-up that has not yet gone out must never reach a customer whose order was cancelled.
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pending)
        {
            await CancelScheduledAsync(followUp, cancellationToken);
        }
    }

    public async Task<SmsNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Repeating a resend under the same key must not send a second message.
        var existing = await _notifications.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }

        var body = string.IsNullOrEmpty(original.Body)
            ? $"eShop: an update about your order #{original.OrderId}."
            : original.Body;

        var resend = new SmsNotification(original.OrderId, original.OwnerId, NotificationType.Resend,
            original.ToNumber, body, idempotencyKey: idempotencyKey);

        // Honour the removal rule: never send again to a number the shopper has taken off file.
        var stillOnFile = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByValueForOwnerSpecification(original.OwnerId, original.ToNumber), cancellationToken);
        if (stillOnFile is null)
        {
            resend.MarkRecipientRemoved();
            await _notifications.AddAsync(resend, cancellationToken);
            _logger.LogWarning("Resend for notification {0} skipped: destination no longer on file.", notificationId);
            return resend;
        }

        await TrySendAsync(resend, () => _sms.SendAsync(resend.ToNumber, body, cancellationToken));
        await _notifications.AddAsync(resend, cancellationToken);
        _logger.LogInformation("Resent notification {0} as {1} (status {2}).", notificationId, resend.Id, resend.Status);
        return resend;
    }

    public async Task<SmsNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (notification.WasAcceptedByProvider)
        {
            try
            {
                await _sms.RedactContentAsync(notification.MessageSid!, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider content disposal failed for notification {0}: {1}", notificationId, ex.Message);
            }
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Content disposed for notification {0}.", notificationId);
        return notification;
    }

    public async Task RefreshDeliveryStateAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (!notification.WasAcceptedByProvider || notification.IsTerminal)
            {
                continue;
            }

            try
            {
                var state = await _sms.FetchAsync(notification.MessageSid!, cancellationToken);
                if (state is not null)
                {
                    notification.ApplyDeliveryState(state.Status, state.ErrorCode, state.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh delivery state for notification {0}: {1}", notification.Id, ex.Message);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _sms.ListSentMessagesAsync(from, to, cancellationToken);
        var eshopNotifications = await _notifications.ListAsync(new SentNotificationsInRangeSpecification(from, to), cancellationToken);

        var eshopBySid = eshopNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eshopOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (eshopBySid.TryGetValue(message.Sid, out var notification))
            {
                matched.Add(new ReconciliationEntry(message.Sid, message.Status, notification.Status,
                    notification.Id, notification.OrderId, "matched"));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(message.Sid, message.Status, null, null, null,
                    "provider-only: the provider sent this but eShop has no record of it"));
            }
        }

        foreach (var notification in eshopNotifications)
        {
            if (notification.MessageSid is null || !providerBySid.ContainsKey(notification.MessageSid))
            {
                eshopOnly.Add(new ReconciliationEntry(notification.MessageSid, null, notification.Status,
                    notification.Id, notification.OrderId,
                    "eShop-only: eShop recorded this but the provider did not return it for the range"));
            }
        }

        return new ReconciliationReport(from, to, providerMessages.Count, eshopNotifications.Count,
            matched.Count, matched, providerOnly, eshopOnly);
    }

    private async Task<IReadOnlyList<ContactNumber>> GetOwnerNumbersAsync(string ownerId)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation("No contact number on file for order owner; no SMS will be sent.");
        }
        return numbers;
    }

    private async Task<SmsNotification> SendNowAndRecordAsync(int orderId, string ownerId, NotificationType type,
        string toNumber, string body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var notification = new SmsNotification(orderId, ownerId, type, toNumber, body, idempotencyKey: idempotencyKey);
        await TrySendAsync(notification, () => _sms.SendAsync(toNumber, body, cancellationToken));
        await _notifications.AddAsync(notification, cancellationToken);
        _logger.LogInformation("Notification {0} ({1}) for order {2} recorded with status {3}.",
            notification.Id, type, orderId, notification.Status);
        return notification;
    }

    private async Task<SmsNotification> ScheduleAndRecordAsync(int orderId, string ownerId, string toNumber,
        string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new SmsNotification(orderId, ownerId, NotificationType.DeliveryFollowUp, toNumber, body,
            isScheduled: true, scheduledFor: sendAt);
        await TrySendAsync(notification, () => _sms.ScheduleAsync(toNumber, body, sendAt, cancellationToken));
        await _notifications.AddAsync(notification, cancellationToken);
        _logger.LogInformation("Follow-up {0} for order {1} scheduled with provider for {2:o} (status {3}).",
            notification.Id, orderId, sendAt, notification.Status);
        return notification;
    }

    private async Task CancelScheduledAsync(SmsNotification followUp, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sms.CancelScheduledAsync(followUp.MessageSid!, cancellationToken);
            if (result.Accepted)
            {
                followUp.ApplyDeliveryState(result.Status ?? "canceled", result.ErrorCode, result.ErrorMessage);
            }
            else
            {
                followUp.MarkCanceled();
            }
        }
        catch (Exception ex)
        {
            // Even if the provider call is momentarily unreachable, mark our intent so we do not
            // report the follow-up as still live; and never let this fail the cancel operation.
            followUp.MarkCanceled();
            _logger.LogWarning("Provider cancel of follow-up {0} raised {1}; recorded as canceled locally.",
                followUp.Id, ex.Message);
        }

        await _notifications.UpdateAsync(followUp, cancellationToken);
        _logger.LogInformation("Follow-up {0} for order {1} called off (status {2}).",
            followUp.Id, followUp.OrderId, followUp.Status);
    }

    /// <summary>
    /// Submit a message to the provider and fold the outcome into the notification, without ever
    /// letting a provider failure escape to the caller.
    /// </summary>
    private async Task TrySendAsync(SmsNotification notification, Func<Task<SmsSendResult>> send)
    {
        try
        {
            var result = await send();
            notification.ApplyProviderResult(result.MessageSid, result.Status, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            notification.MarkSubmissionFailed(ex.Message);
            _logger.LogWarning("SMS submission failed for order {0} notification (type {1}): {2}",
                notification.OrderId, notification.Type, ex.Message);
        }
    }
}
