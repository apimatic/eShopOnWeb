using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order SMS notifications. Messaging must never fail the underlying
/// order operation, so every provider interaction is captured into a notification
/// record instead of throwing.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider outcomes that will not change anymore; no point re-polling them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", OrderNotification.SendFailedStatus
    };

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!";
        return SendAndRecordAsync(order, NotificationType.OrderPlaced, body, scheduledFor: null, idempotencyKey: null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: good news! Your order #{order.Id} has been dispatched and is on its way.";
        await SendAndRecordAsync(order, NotificationType.OrderDispatched, body, scheduledFor: null, idempotencyKey: null, cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        await SendAndRecordAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt, idempotencyKey: null, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. Sorry for any inconvenience.";
        await SendAndRecordAsync(order, NotificationType.OrderCancelled, body, scheduledFor: null, idempotencyKey: null, cancellationToken);

        // Call off any follow-up that has not gone out yet: a cancelled order must
        // never produce a "how did the delivery go" message.
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpec(order.Id), cancellationToken);
        foreach (var followUp in notifications.Where(n =>
                     n.Type == NotificationType.DeliveryFollowUp &&
                     n.ProviderMessageSid != null &&
                     n.Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var result = await _smsProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (result.Success && result.Message != null)
                {
                    followUp.UpdateProviderStatus(result.Message.Status ?? "canceled", result.Message.ErrorMessage);
                }
                else
                {
                    _logger.LogWarning("Could not cancel scheduled follow-up {MessageSid} for order {OrderId}: {Error}",
                        followUp.ProviderMessageSid, order.Id, result.Error ?? "unknown");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error cancelling scheduled follow-up {MessageSid} for order {OrderId}" + ": " + ex.Message, followUp.ProviderMessageSid!, order.Id);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);

        foreach (var notification in notifications.Where(n =>
                     n.ProviderMessageSid != null && !TerminalStatuses.Contains(n.Status)))
        {
            try
            {
                var message = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                if (message != null && !string.Equals(message.Status, notification.Status, StringComparison.OrdinalIgnoreCase))
                {
                    notification.UpdateProviderStatus(message.Status ?? notification.Status, message.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error refreshing status for message {MessageSid}" + ": " + ex.Message, notification.ProviderMessageSid!);
            }
        }

        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpec(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendResult { Outcome = ResendOutcome.AlreadyProcessed, Notification = existing };
        }

        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return new ResendResult { Outcome = ResendOutcome.NotFound };
        }

        if (notification.ContentDisposed || notification.Body is null)
        {
            return new ResendResult { Outcome = ResendOutcome.ContentDisposed };
        }

        // A removed contact number must never be messaged again.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(notification.ContactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.OwnerId != notification.BuyerId)
        {
            return new ResendResult { Outcome = ResendOutcome.ContactNumberRemoved };
        }

        var resend = new OrderNotification(notification.OrderId, notification.BuyerId, notification.ContactNumberId,
            NotificationType.Resend, notification.Body, scheduledFor: null, idempotencyKey: idempotencyKey);

        await DispatchAsync(resend, contactNumber.PhoneNumber, scheduledFor: null, cancellationToken);

        return new ResendResult { Outcome = ResendOutcome.Resent, Notification = resend };
    }

    public async Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (notification.ContentDisposed)
        {
            return true;
        }

        if (notification.ProviderMessageSid != null)
        {
            // Erase the body at the provider too, not just locally.
            var redacted = await _smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            if (!redacted)
            {
                return false;
            }
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsProvider.ListMessagesFromSendingNumberAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new NotificationsCreatedInRangeSpec(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport { From = from, To = to };

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                report.Matched.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    NotificationId = local.Id,
                    LocalStatus = local.Status,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent
                });
            }
            else
            {
                report.MissingLocally.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent
                });
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));
        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid is null || !providerSids.Contains(local.ProviderMessageSid))
            {
                report.MissingAtProvider.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    NotificationId = local.Id,
                    LocalStatus = local.Status,
                    DateSent = local.CreatedAt
                });
            }
        }

        return report;
    }

    private async Task SendAndRecordAsync(Order order, NotificationType type, string body, DateTimeOffset? scheduledFor,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            // Notify the shopper's most recently registered number; a shopper with no
            // number on file is simply not messaged.
            var contactNumber = (await _contactNumberRepository.ListAsync(
                new ContactNumbersByOwnerSpec(order.BuyerId), cancellationToken)).FirstOrDefault();
            if (contactNumber is null)
            {
                _logger.LogInformation("Order {OrderId}: buyer has no contact number; skipping {Type} notification", order.Id, type);
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body, scheduledFor, idempotencyKey);
            await DispatchAsync(notification, contactNumber.PhoneNumber, scheduledFor, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: unexpected error sending {Type} notification" + ": " + ex.Message, order.Id, type);
        }
    }

    private async Task DispatchAsync(OrderNotification notification, string to, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            var result = scheduledFor.HasValue
                ? await _smsProvider.ScheduleMessageAsync(to, notification.Body!, scheduledFor.Value, cancellationToken)
                : await _smsProvider.SendMessageAsync(to, notification.Body!, cancellationToken);

            if (result.Success && result.Message != null)
            {
                notification.MarkAccepted(result.Message.Sid, result.Message.Status ?? "unknown");
            }
            else
            {
                notification.MarkSendFailed(result.Error ?? "provider rejected the message");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Provider call failed for order {OrderId} notification" + ": " + ex.Message, notification.OrderId);
            notification.MarkSendFailed("provider call failed");
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }
}


