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

public class OrderNotificationService : IOrderNotificationService
{
    // The delivery follow-up is queued with the provider this far after dispatch.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly ITextMessageProvider _messageProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        ITextMessageProvider messageProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messageProvider = messageProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total: ${order.Total():0.00}. We'll text you when it ships.";
        return NotifyAsync(order, OrderNotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news — order #{order.Id} is on its way!";
        await NotifyAsync(order, OrderNotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: how did the delivery of order #{order.Id} go? We'd love to hear from you.";
        await NotifyAsync(order, OrderNotificationType.DeliveryFollowUp, followUpBody, cancellationToken,
            scheduleFor: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay));
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If you didn't request this, please contact support.";
        await NotifyAsync(order, OrderNotificationType.OrderCancelled, body, cancellationToken);

        // A queued delivery follow-up for a cancelled order must never reach the shopper.
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);
        var pendingFollowUps = notifications
            .Where(n => n.Type == OrderNotificationType.DeliveryFollowUp
                        && n.MessageSid != null
                        && n.Status is "scheduled" or "accepted" or "queued" or OrderNotification.LocalStatusPending)
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var cancelled = await _messageProvider.CancelScheduledMessageAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateDeliveryOutcome(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage, cancelled.DateSent);
                _logger.LogInformation("Cancelled scheduled follow-up {MessageSid} for order {OrderId}", followUp.MessageSid!, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {MessageSid} for order {OrderId}", followUp.MessageSid!, order.Id);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        // No provider callback URL exists, so delivery outcomes are pulled from the provider.
        foreach (var notification in notifications.Where(n => n.MessageSid != null))
        {
            try
            {
                var current = await _messageProvider.FetchMessageAsync(notification.MessageSid!, cancellationToken);
                notification.UpdateDeliveryOutcome(current.Status, current.ErrorCode, current.ErrorMessage, current.DateSent);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not refresh delivery outcome for message {MessageSid}", notification.MessageSid!);
            }
        }

        return notifications;
    }

    public async Task<NotificationResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            // Same key seen before: replay the recorded outcome instead of sending again.
            return new NotificationResendResult(true, existing, null, WasIdempotentReplay: true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return new NotificationResendResult(false, null, "Notification not found.");
        }
        if (original.ContentRedacted || original.Body == null)
        {
            return new NotificationResendResult(false, null, "The content of this message has been disposed of and can no longer be sent.");
        }

        // Send to the shopper's currently registered number; a removed number is never used again.
        var recipient = await GetCurrentRecipientAsync(original.BuyerId, cancellationToken);
        if (recipient == null)
        {
            return new NotificationResendResult(false, null, "The shopper has no registered contact number.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, recipient.PhoneNumber,
            original.Type, original.Body, idempotencyKey: idempotencyKey);
        await SendAndRecordAsync(resend, cancellationToken);
        return new NotificationResendResult(true, resend, null);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }
        if (notification.ContentRedacted)
        {
            return true;
        }

        // Dispose of the text at the provider first; only hide it locally once
        // the provider no longer returns it either.
        if (notification.MessageSid != null)
        {
            await _messageProvider.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of message content for notification {NotificationId}", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messageProvider.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        var matchedSids = new HashSet<string>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.MessageSid, out var local))
            {
                matchedSids.Add(message.MessageSid);
                entries.Add(new ReconciliationEntry(message.MessageSid, local.Id, message.Status, message.DateSent, "Matched"));
            }
            else
            {
                entries.Add(new ReconciliationEntry(message.MessageSid, null, message.Status, message.DateSent, "ProviderOnly"));
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.MessageSid == null || !matchedSids.Contains(local.MessageSid))
            {
                entries.Add(new ReconciliationEntry(local.MessageSid, local.Id, local.Status, local.SentAt, "LocalOnly"));
            }
        }

        return new ReconciliationReport(from, to, providerMessages.Count, localNotifications.Count, entries);
    }

    private async Task NotifyAsync(Order order, OrderNotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduleFor = null)
    {
        var recipient = await GetCurrentRecipientAsync(order.BuyerId, cancellationToken);
        if (recipient == null)
        {
            _logger.LogInformation("Order {OrderId}: shopper has no contact number; no {NotificationType} message sent", order.Id, type);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, recipient.PhoneNumber, type, body, scheduleFor);
        await SendAndRecordAsync(notification, cancellationToken, scheduleFor);
    }

    private async Task SendAndRecordAsync(OrderNotification notification, CancellationToken cancellationToken,
        DateTimeOffset? scheduleFor = null)
    {
        scheduleFor ??= notification.ScheduledFor;
        try
        {
            var message = scheduleFor.HasValue
                ? await _messageProvider.ScheduleMessageAsync(notification.RecipientNumber, notification.Body!, scheduleFor.Value, cancellationToken)
                : await _messageProvider.SendMessageAsync(notification.RecipientNumber, notification.Body!, cancellationToken);
            notification.MarkAccepted(message.MessageSid, message.Status, message.DateSent);
        }
        catch (Exceptions.TextMessageProviderException pex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            // Detail may reference the destination number: store it, never log it.
            notification.MarkFailed(pex.Detail ?? pex.Message);
            _logger.LogError("Provider rejected {NotificationType} message for order {OrderId}: {SafeReason}", notification.Type, notification.OrderId, pex.Message);
        }
        catch (Exception ex)
        {
            notification.MarkFailed("Unexpected error while submitting the message to the provider.");
            _logger.LogError(ex, "Failed to submit {NotificationType} message for order {OrderId}", notification.Type, notification.OrderId);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task<ContactNumber?> GetCurrentRecipientAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
        return numbers.LastOrDefault();
    }
}
