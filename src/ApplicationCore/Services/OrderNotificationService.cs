using System;
using System.Collections.Generic;
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
    // How long after dispatch the delivery follow-up goes out. Queued with the
    // provider (not held in this application).
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: Thank you! Your order #{order.Id} has been placed. We'll text you when it's on its way.";
        await SendAndRecordAsync(order, OrderNotificationType.OrderPlaced, body, cancellationToken: cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: Good news! Your order #{order.Id} has been dispatched and is on its way.";
        await SendAndRecordAsync(order, OrderNotificationType.OrderDispatched, body, cancellationToken: cancellationToken);

        var followUpBody = $"eShop: Your order #{order.Id} should have arrived by now. How did the delivery go? We'd love your feedback.";
        await SendAndRecordAsync(order, OrderNotificationType.DeliveryFollowUp, followUpBody,
            scheduleFor: DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken: cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: Your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendAndRecordAsync(order, OrderNotificationType.OrderCancelled, body, cancellationToken: cancellationToken);

        await CancelPendingFollowUpsAsync(order, cancellationToken);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("Resend under an already-used idempotency key; returning the original resend record {NotificationId}.", existing.Id);
            return new ResendResult(existing, idempotentReplay: true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        Guard.Against.Null(original, nameof(notificationId));

        if (original.ContentRedacted)
        {
            throw new InvalidOperationException("The content of this message has been disposed of and it can no longer be re-sent.");
        }

        // A deleted contact number must never be messaged again.
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(original.BuyerId), cancellationToken);
        if (!contactNumbers.Any(c => c.PhoneNumber == original.ToNumber))
        {
            throw new InvalidOperationException("The destination contact number is no longer registered for this shopper.");
        }

        await RefreshStatusAsync(original, cancellationToken);
        if (!original.HasTerminalStatus ||
            !(original.Status == "failed" || original.Status == "undelivered" || original.Status == "send-failed"))
        {
            throw new InvalidOperationException($"Only messages that did not reach the shopper can be re-sent (current status: {original.Status}).");
        }

        var result = await _smsGateway.SendMessageAsync(original.ToNumber, original.Body, cancellationToken);
        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber, original.Type,
            original.Body, result.MessageSid, result.Status, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);
        return new ResendResult(resend, idempotentReplay: false);
    }

    public async Task DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        Guard.Against.Null(notification, nameof(notificationId));

        if (notification.ContentRedacted)
        {
            return;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _smsGateway.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications.Where(n => !n.HasTerminalStatus))
        {
            await RefreshStatusAsync(notification, cancellationToken);
        }
    }

    public async Task CancelPendingNotificationsToNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        var pending = notifications
            .Where(n => n.ToNumber == phoneNumber && n.Status == "scheduled" && n.ProviderMessageSid != null);

        foreach (var notification in pending)
        {
            try
            {
                var result = await _smsGateway.CancelScheduledMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.UpdateProviderStatus(result.Status, null, null);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled notification {NotificationId} for a removed contact number: {Error}", notification.Id, ex.Message);
            }
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsGateway.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderMessageCount = providerMessages.Count,
            LocalNotificationCount = localNotifications.Count
        };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.MessageSid, out var local))
            {
                report.Matched.Add(new ReconciliationEntry
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = message.MessageSid,
                    LocalStatus = local.Status,
                    ProviderStatus = message.Status,
                    ProviderDateSent = message.DateSent
                });
            }
            else
            {
                report.MissingFromEShop.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.MessageSid,
                    ProviderStatus = message.Status,
                    ProviderDateSent = message.DateSent
                });
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.MessageSid));
        foreach (var local in localNotifications.Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid)))
        {
            if (!providerSids.Contains(local.ProviderMessageSid!))
            {
                report.MissingFromProvider.Add(new ReconciliationEntry
                {
                    NotificationId = local.Id,
                    ProviderMessageSid = local.ProviderMessageSid,
                    LocalStatus = local.Status
                });
            }
        }

        return report;
    }

    private async Task SendAndRecordAsync(Order order, OrderNotificationType type, string body,
        DateTimeOffset? scheduleFor = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var contactNumbers = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            var destination = contactNumbers.FirstOrDefault();
            if (destination == null)
            {
                _logger.LogInformation("Buyer has no contact number on file; skipping {NotificationType} notification for order {OrderId}.", type, order.Id);
                return;
            }

            string? messageSid = null;
            var status = "send-failed";
            try
            {
                var result = scheduleFor.HasValue
                    ? await _smsGateway.ScheduleMessageAsync(destination.PhoneNumber, body, scheduleFor.Value, cancellationToken)
                    : await _smsGateway.SendMessageAsync(destination.PhoneNumber, body, cancellationToken);
                messageSid = result.MessageSid;
                status = result.Status;
            }
            catch (Exception ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                _logger.LogWarning("Failed to submit {NotificationType} notification for order {OrderId}: {Error}", type, order.Id, ex.Message);
            }

            var record = new OrderNotification(order.Id, order.BuyerId, destination.PhoneNumber, type, body,
                messageSid, status, scheduledFor: scheduleFor);
            await _notificationRepository.AddAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification bookkeeping failed for order {OrderId}: {Error}", order.Id, ex.Message);
        }
    }

    private async Task CancelPendingFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
        var pendingFollowUps = notifications
            .Where(n => n.Type == OrderNotificationType.DeliveryFollowUp && n.Status == "scheduled" && n.ProviderMessageSid != null);

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var result = await _smsGateway.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderStatus(result.Status, null, null);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}: {Error}", followUp.Id, order.Id, ex.Message);
            }
        }
    }

    private async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var details = await _smsGateway.FetchMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderStatus(details.Status, details.ErrorCode, details.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh status for notification {NotificationId}: {Error}", notification.Id, ex.Message);
        }
    }
}
