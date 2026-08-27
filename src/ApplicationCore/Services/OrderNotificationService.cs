using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed (total ${order.Total():0.00}). We'll text you when it ships.";
        return SendAndRecordAsync(order, NotificationType.OrderPlaced, body, null, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchBody = $"eShop: good news — your order #{order.Id} is on its way.";
        await SendAndRecordAsync(order, NotificationType.OrderDispatched, dispatchBody, null, null, cancellationToken);

        // The follow-up is queued with the provider itself (scheduled send), not held in-app.
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to know.";
        await SendAndRecordAsync(order, NotificationType.DeliveryFollowUp, followUpBody,
            DateTimeOffset.UtcNow.Add(FollowUpDelay), null, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. Reply here or contact support if this is unexpected.";
        await SendAndRecordAsync(order, NotificationType.OrderCancelled, body, null, null, cancellationToken);

        // A queued follow-up for a cancelled order must never reach the shopper.
        var pendingFollowUps = (await _notificationRepository.ListAsync(cancellationToken))
            .Where(n => n.OrderId == order.Id
                        && n.Type == NotificationType.DeliveryFollowUp
                        && n.MessageSid != null
                        && string.Equals(n.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var cancelled = await _messagingProvider.CancelScheduledMessageAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateFromProvider(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                _logger.LogInformation("Cancelled scheduled follow-up {MessageSid} for order {OrderId}", followUp.MessageSid ?? "n/a", order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {MessageSid} for order {OrderId}", followUp.MessageSid ?? "n/a", order.Id);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey);

        var existing = (await _notificationRepository.ListAsync(cancellationToken))
            .FirstOrDefault(n => n.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            _logger.LogInformation("Idempotent resend: key already used by notification {NotificationId}", existing.Id);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null) throw new NotificationNotFoundException(notificationId);

        await RefreshFromProviderAsync(original, cancellationToken);

        if (!string.Equals(original.Status, "failed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(original.Status, "undelivered", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotificationConflictException(
                $"Notification {notificationId} last reported status '{original.Status}'; only messages that did not reach the shopper can be re-sent.");
        }
        if (original.ContentRedacted || original.Body == null)
        {
            throw new NotificationConflictException($"Notification {notificationId} content has been disposed of and can no longer be sent.");
        }

        // A deleted contact number must never be messaged again.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != original.BuyerId)
        {
            throw new NotificationConflictException($"The destination for notification {notificationId} is no longer registered; it must not be messaged again.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            NotificationType.Resend, original.Body, idempotencyKey);

        await TrySendAsync(resend, contactNumber.PhoneNumber, null, cancellationToken);
        await _notificationRepository.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null) throw new NotificationNotFoundException(notificationId);

        if (notification.ContentRedacted) return;

        if (notification.MessageSid != null)
        {
            // Redact at the provider too — the text must not remain retrievable there.
            await _messagingProvider.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId} (message {MessageSid})", notification.Id, notification.MessageSid ?? "n/a");
    }

    public async Task CancelPendingForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        // Only provider-scheduled messages can still be called off; anything already
        // queued/sent is on its way and is handled by the provider.
        var pending = (await _notificationRepository.ListAsync(cancellationToken))
            .Where(n => n.ContactNumberId == contactNumberId && n.MessageSid != null
                        && string.Equals(n.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var notification in pending)
        {
            try
            {
                var cancelled = await _messagingProvider.CancelScheduledMessageAsync(notification.MessageSid!, cancellationToken);
                notification.UpdateFromProvider(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel pending message {MessageSid} for removed contact number {ContactNumberId}",
                    notification.MessageSid ?? "n/a", contactNumberId);
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.MessageSid == null || notification.IsInTerminalState) return;

        try
        {
            var message = await _messagingProvider.GetMessageAsync(notification.MessageSid, cancellationToken);
            notification.UpdateFromProvider(message.Status, message.ErrorCode, message.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh status for message {MessageSid}; returning last known state", notification.MessageSid);
        }
    }

    public async Task<IReadOnlyList<ReconciliationItem>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messagingProvider.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = (await _notificationRepository.ListAsync(cancellationToken))
            .Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .ToList();

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var items = new List<ReconciliationItem>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                items.Add(new ReconciliationItem
                {
                    MessageSid = message.Sid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderStatus = message.Status,
                    LocalStatus = local.Status,
                    DateSent = message.DateSent,
                    Disposition = "Matched"
                });
            }
            else
            {
                items.Add(new ReconciliationItem
                {
                    MessageSid = message.Sid,
                    ProviderStatus = message.Status,
                    DateSent = message.DateSent,
                    Disposition = "MissingLocally"
                });
            }
        }

        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));
        foreach (var local in localNotifications)
        {
            if (local.MessageSid == null)
            {
                items.Add(new ReconciliationItem
                {
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status,
                    Disposition = "NotSent"
                });
            }
            else if (!providerSids.Contains(local.MessageSid))
            {
                items.Add(new ReconciliationItem
                {
                    MessageSid = local.MessageSid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status,
                    Disposition = "MissingAtProvider"
                });
            }
        }

        return items;
    }

    private async Task SendAndRecordAsync(Order order, NotificationType type, string body, DateTimeOffset? scheduleAt,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        // The most recently registered number is the shopper's current contact point.
        var contactNumber = (await _contactNumberRepository.ListAsync(cancellationToken))
            .Where(c => c.BuyerId == order.BuyerId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        if (contactNumber == null)
        {
            _logger.LogInformation("Buyer has no contact number on file; skipping {Type} notification for order {OrderId}", type, order.Id);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body, idempotencyKey);
        await TrySendAsync(notification, contactNumber.PhoneNumber, scheduleAt, cancellationToken);
        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task TrySendAsync(OrderNotification notification, string destination, DateTimeOffset? scheduleAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messagingProvider.SendMessageAsync(destination, notification.Body!, scheduleAt, cancellationToken);
            notification.MarkAccepted(message.Sid, message.Status);
            _logger.LogInformation("Notification {Type} for order {OrderId} accepted by provider as {MessageSid} (status {Status})",
                notification.Type, notification.OrderId, message.Sid, message.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkSendFailed(ex.Message);
            _logger.LogError(ex, "Failed to send {Type} notification for order {OrderId}", notification.Type, notification.OrderId);
        }
    }
}
