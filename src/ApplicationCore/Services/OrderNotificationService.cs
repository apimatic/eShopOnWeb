using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider statuses beyond which no further change is expected.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private const string ScheduledStatus = "scheduled";
    private const string SendFailedStatus = "failed";

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessageProvider _messageProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IMessageProvider messageProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messageProvider = messageProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you for shopping with us!";
        return NotifyAllContactNumbersAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news — your order #{order.Id} has been dispatched and is on its way.";
        await NotifyAllContactNumbersAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: your order #{order.Id} was dispatched a few days ago. How did the delivery go? We'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAllContactNumbersAsync(order, NotificationType.DeliveryFollowUp, followUpBody, cancellationToken, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await NotifyAllContactNumbersAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrdersSpecification(orderIds), cancellationToken);
        await RefreshOutcomesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOrderRequestException("An idempotency key is required to resend a notification.");
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(existing, true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new NotFoundException($"Notification {notificationId} was not found.");
        }

        if (original.IsContentDisposed || original.Body is null)
        {
            throw new NotificationConflictException($"Notification {notificationId} content has been disposed of and can no longer be sent.");
        }

        var stillRegistered = await _contactNumberRepository.CountAsync(
            new ContactNumberByOwnerAndNumberSpecification(original.BuyerId, original.ToNumber), cancellationToken);
        if (stillRegistered == 0)
        {
            throw new NotificationConflictException("The destination number is no longer on file for this shopper; the message must not be sent to it again.");
        }

        var resend = await SendAndRecordAsync(
            original.OrderId, original.BuyerId, NotificationType.Resend, original.ToNumber, original.Body,
            cancellationToken, scheduledFor: null, resendOfNotificationId: original.Id, idempotencyKey: idempotencyKey);

        return new ResendResult(resend, false);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new NotFoundException($"Notification {notificationId} was not found.");
        }

        if (!notification.IsContentDisposed && notification.ProviderMessageSid is not null)
        {
            await _messageProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.DisposeContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _messageProvider.ListMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(new NotificationsWithProviderSidSpecification(), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid is not null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        var matchedSids = new HashSet<string>();

        foreach (var message in providerMessages)
        {
            matchedSids.Add(message.Sid);
            var known = localBySid.TryGetValue(message.Sid, out var local);
            entries.Add(new ReconciliationEntry(
                message.Sid,
                message.Status,
                message.To,
                message.DateSent,
                message.DateCreated,
                known ? local!.Id : null,
                known ? local!.OrderId : null,
                known ? local!.ProviderStatus : null,
                known ? "Matched" : "MissingLocally"));
        }

        foreach (var local in localNotifications.Where(n =>
                     n.ProviderMessageSid is not null &&
                     !matchedSids.Contains(n.ProviderMessageSid!) &&
                     n.CreatedAt >= from && n.CreatedAt <= to))
        {
            entries.Add(new ReconciliationEntry(
                local.ProviderMessageSid!,
                null,
                null,
                null,
                null,
                local.Id,
                local.OrderId,
                local.ProviderStatus,
                "MissingAtProvider"));
        }

        return entries
            .OrderByDescending(e => e.DateSent ?? e.DateCreated ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task NotifyAllContactNumbersAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken, DateTimeOffset? scheduledFor = null)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);

        foreach (var contactNumber in contactNumbers)
        {
            await SendAndRecordAsync(order.Id, order.BuyerId, type, contactNumber.PhoneNumber, body, cancellationToken, scheduledFor);
        }
    }

    private async Task<OrderNotification> SendAndRecordAsync(
        int orderId,
        string buyerId,
        NotificationType type,
        string toNumber,
        string body,
        CancellationToken cancellationToken,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderNotification notification;
        try
        {
            var message = scheduledFor.HasValue
                ? await _messageProvider.ScheduleMessageAsync(toNumber, body, scheduledFor.Value, cancellationToken)
                : await _messageProvider.SendMessageAsync(toNumber, body, cancellationToken);

            notification = new OrderNotification(
                orderId, buyerId, type, toNumber, body,
                message.Sid, message.Status, message.ErrorCode, message.ErrorMessage,
                scheduledFor, resendOfNotificationId, idempotencyKey);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            // Never log the destination number; provider error messages may contain it.
            _logger.LogWarning("Failed to submit {NotificationType} notification for order {OrderId} to the provider (provider error code {ProviderErrorCode}).",
                type, orderId, (object?)((ex as MessageProviderException)?.ProviderErrorCode) ?? "n/a");
            notification = new OrderNotification(
                orderId, buyerId, type, toNumber, body,
                null, SendFailedStatus,
                (ex as MessageProviderException)?.ProviderErrorCode, ex.Message,
                scheduledFor, resendOfNotificationId, idempotencyKey);
        }

        return await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        var followUps = notifications.Where(n => n.NotificationType == NotificationType.DeliveryFollowUp);

        foreach (var followUp in followUps)
        {
            if (followUp.ProviderMessageSid is null)
            {
                continue;
            }

            try
            {
                var current = await _messageProvider.GetMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                if (current is null)
                {
                    continue;
                }

                followUp.UpdateProviderOutcome(current.Status, current.ErrorCode, current.ErrorMessage);

                if (string.Equals(current.Status, ScheduledStatus, StringComparison.OrdinalIgnoreCase))
                {
                    var canceled = await _messageProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                    followUp.UpdateProviderOutcome(canceled.Status, canceled.ErrorCode, canceled.ErrorMessage);
                    _logger.LogInformation("Canceled scheduled follow-up {MessageSid} for order {OrderId}.", followUp.ProviderMessageSid, orderId);
                }

                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {MessageSid} for order {OrderId} (provider error code {ProviderErrorCode}).",
                    followUp.ProviderMessageSid, orderId, (object?)((ex as MessageProviderException)?.ProviderErrorCode) ?? "n/a");
            }
        }
    }

    private async Task RefreshOutcomesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid is null ||
                (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus)))
            {
                continue;
            }

            try
            {
                var current = await _messageProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (current is null)
                {
                    continue;
                }

                notification.UpdateProviderOutcome(current.Status, current.ErrorCode, current.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh outcome for provider message {MessageSid} (provider error code {ProviderErrorCode}).",
                    notification.ProviderMessageSid, (object?)((ex as MessageProviderException)?.ProviderErrorCode) ?? "n/a");
            }
        }
    }
}
