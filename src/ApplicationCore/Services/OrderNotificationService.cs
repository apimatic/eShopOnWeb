using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ISmsSender _smsSender;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        ISmsSender smsSender,
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IAppLogger<OrderNotificationService> logger)
    {
        _smsSender = smsSender;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(nameof(NotifyOrderPlacedAsync), order.Id, async () =>
        {
            var numbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken);
            var body = $"Your eShop order #{order.Id} has been placed. Thank you for shopping with us!";
            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderPlaced, body, cancellationToken);
            }
        });
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(nameof(NotifyOrderDispatchedAsync), order.Id, async () =>
        {
            var numbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken);
            var dispatchedBody = $"Good news! Your eShop order #{order.Id} is on its way.";
            var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";
            var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderDispatched, dispatchedBody, cancellationToken);
                await ScheduleFollowUpAsync(order, number.PhoneNumber, followUpBody, sendAt, cancellationToken);
            }
        });
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(nameof(NotifyOrderCancelledAsync), order.Id, async () =>
        {
            // Call off any delivery follow-up still queued at the provider first: asking a customer
            // how their delivery went for a cancelled order is exactly the incident to prevent.
            await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

            var numbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken);
            var body = $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
            foreach (var number in numbers)
            {
                await SendImmediateAsync(order, number.PhoneNumber, NotificationType.OrderCancelled, body, cancellationToken);
            }
        });
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Idempotency: a repeat under the same key returns the message the first request produced,
        // without sending a second one.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return ResendResult.IdempotentReplay(existing);
        }

        var source = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (source is null)
        {
            return ResendResult.NotFound();
        }

        if (source.ContentRedacted || string.IsNullOrEmpty(source.MessageBody))
        {
            return ResendResult.Rejected("The content of this message has been disposed of and cannot be resent.");
        }

        var resend = new OrderNotification(source.OrderId, source.BuyerId, NotificationType.Resend, source.ToPhoneNumber, source.MessageBody);
        resend.MarkAsResendOf(source.Id, idempotencyKey);

        // Persist first so the idempotency key is committed even if the send throws — a retry under
        // the same key must never produce a second message.
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var sent = await _smsSender.SendAsync(source.ToPhoneNumber, source.MessageBody, cancellationToken);
            resend.MarkQueued(sent.Sid, sent.Status);
            resend.SetProviderDateSent(sent.DateSent);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed();
            _logger.LogWarning("Resend of notification {NotificationId} for order {OrderId} could not be handed to the provider: {Error}",
                source.Id, source.OrderId, ex.Message);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return ResendResult.Sent(resend);
    }

    public async Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return ContentDisposalResult.NotFound();
        }

        var providerRedacted = false;
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _smsSender.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
                providerRedacted = true;
            }
            catch (Exception ex)
            {
                // Do not claim disposal we could not carry out at the provider.
                _logger.LogWarning("Provider redaction failed for notification {NotificationId}: {Error}", notificationId, ex.Message);
                return ContentDisposalResult.Failed("The message content could not be disposed of at the provider. No local change was made.");
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return ContentDisposalResult.Disposed(providerRedacted);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Provider side: the provider's own record of messages sent from our configured number in range.
        var providerMessages = await _smsSender.ListSentMessagesAsync(from, to, cancellationToken);

        // eShop side: notifications we believe we handed to the provider in range.
        var eShopNotifications = await _notificationRepository.ListAsync(
            new NotificationsSentInRangeSpecification(from, to), cancellationToken);

        var byProviderSid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .ToDictionary(m => m.Sid, StringComparer.OrdinalIgnoreCase);
        var byEShopSid = eShopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();
        var eShopOnly = new List<ReconciliationEntry>();

        foreach (var provider in byProviderSid.Values)
        {
            if (byEShopSid.TryGetValue(provider.Sid, out var eShop))
            {
                matched.Add(new ReconciliationEntry(provider.Sid, eShop.Id, eShop.OrderId, provider.Status, eShop.Status, provider.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(provider.Sid, null, null, provider.Status, null, provider.DateSent));
            }
        }

        foreach (var eShop in byEShopSid.Values)
        {
            if (!byProviderSid.ContainsKey(eShop.ProviderMessageSid!))
            {
                eShopOnly.Add(new ReconciliationEntry(eShop.ProviderMessageSid, eShop.Id, eShop.OrderId, null, eShop.Status, eShop.ProviderDateSent));
            }
        }

        return new ReconciliationReport(from, to, matched, providerOnly, eShopOnly);
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null || notification.IsTerminal())
            {
                continue;
            }

            try
            {
                var status = await _smsSender.GetStatusAsync(notification.ProviderMessageSid, cancellationToken);
                if (!string.Equals(status, notification.Status, StringComparison.OrdinalIgnoreCase))
                {
                    notification.UpdateStatus(status);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
            }
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool refresh, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        if (refresh)
        {
            await RefreshStatusesAsync(notifications, cancellationToken);
        }
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, bool refresh, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        if (refresh)
        {
            await RefreshStatusesAsync(notifications, cancellationToken);
        }
        return notifications;
    }

    private async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }

    private async Task SendImmediateAsync(Order order, string toPhoneNumber, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, type, toPhoneNumber, body);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        try
        {
            var sent = await _smsSender.SendAsync(toPhoneNumber, body, cancellationToken);
            notification.MarkQueued(sent.Sid, sent.Status);
            notification.SetProviderDateSent(sent.DateSent);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Notification {Type} for order {OrderId} could not be sent: {Error}", type, order.Id, ex.Message);
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task ScheduleFollowUpAsync(Order order, string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, toPhoneNumber, body);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        try
        {
            var scheduled = await _smsSender.ScheduleAsync(toPhoneNumber, body, sendAt, cancellationToken);
            notification.MarkQueued(scheduled.Sid, scheduled.Status, isScheduled: true);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed();
            _logger.LogWarning("Delivery follow-up for order {OrderId} could not be scheduled: {Error}", order.Id, ex.Message);
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notificationRepository.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in pending)
        {
            try
            {
                await _smsSender.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.MarkCancelled();
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}",
                    followUp.Id, orderId, ex.Message);
            }
        }
    }

    private async Task SafelyAsync(string operation, int orderId, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            // A messaging problem must never fail the underlying order operation.
            _logger.LogWarning("{Operation} for order {OrderId} failed but the order operation stands: {Error}",
                operation, orderId, ex.Message);
        }
    }
}
