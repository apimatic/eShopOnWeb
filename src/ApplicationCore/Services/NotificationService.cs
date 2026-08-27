using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationService : INotificationService
{
    /// <summary>How long after dispatch the delivery follow-up is queued for with the provider.</summary>
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    // Delivery outcomes the provider will not change on its own anymore.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessageProvider _messageProvider;
    private readonly IAppLogger<NotificationService> _logger;

    public NotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IMessageProvider messageProvider,
        IAppLogger<NotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messageProvider = messageProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order)
    {
        var body = $"eShop: thank you! Your order #{order.Id} was placed on {order.OrderDate:yyyy-MM-dd}. " +
                   $"Total ${order.Total():0.00}. We'll text you when it's on its way.";
        return NotifyBuyerAsync(order, NotificationType.OrderPlaced, body, sendAtUtc: null);
    }

    public async Task NotifyOrderDispatchedAsync(Order order)
    {
        var body = $"eShop: good news - your order #{order.Id} is on its way!";
        await NotifyBuyerAsync(order, NotificationType.OrderDispatched, body, sendAtUtc: null);

        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to know.";
        await NotifyBuyerAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await NotifyBuyerAsync(order, NotificationType.OrderCancelled, body, sendAtUtc: null);

        // A follow-up that has not yet gone out must never reach the shopper.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledOrderNotificationsSpecification(order.Id));
        foreach (var notification in scheduled)
        {
            try
            {
                var cancelled = await _messageProvider.CancelScheduledMessageAsync(notification.ProviderMessageSid!);
                notification.UpdateProviderState(
                    cancelled?.Status ?? "canceled",
                    cancelled?.ErrorCode,
                    cancelled?.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification);
                _logger.LogInformation("Cancelled scheduled notification {NotificationId} (provider message {MessageSid}) for order {OrderId}",
                    notification.Id, notification.ProviderMessageSid, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled notification {NotificationId} (provider message {MessageSid}) for order {OrderId}: {Error}",
                    notification.Id, notification.ProviderMessageSid, order.Id, ex.Message);
            }
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool refreshStatus)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId));
        if (refreshStatus)
        {
            foreach (var notification in notifications)
            {
                await RefreshStatusAsync(notification);
            }
        }
        return notifications;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository
            .FirstOrDefaultAsync(new OrderNotificationByIdempotencyKeySpecification(idempotencyKey));
        if (existing != null)
        {
            _logger.LogInformation("Idempotency key already used; returning existing notification {NotificationId}", existing.Id);
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId);
        if (original == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }
        if (original.ContentRedacted || original.Body == null)
        {
            throw new NotificationContentRedactedException(notificationId);
        }

        var resend = await SendAndRecordAsync(original.OrderId, original.BuyerId, original.ToNumber,
            original.Type, original.Body, sendAtUtc: null, idempotencyKey);
        return resend;
    }

    public async Task DeleteContentAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification == null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!notification.ContentRedacted && notification.ProviderMessageSid != null)
        {
            await _messageProvider.RedactMessageBodyAsync(notification.ProviderMessageSid);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification);
        _logger.LogInformation("Disposed of content for notification {NotificationId} (provider message {MessageSid})",
            notification.Id, notification.ProviderMessageSid);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var providerMessages = await _messageProvider.ListMessagesAsync(fromUtc, toUtc);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(fromUtc, toUtc));

        var localBySid = localNotifications
            .Where(n => n.ProviderMessageSid != null)
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = new HashSet<string>(providerMessages.Select(m => m.Sid));

        var report = new ReconciliationReport { FromUtc = fromUtc, ToUtc = toUtc };

        foreach (var message in providerMessages)
        {
            var entry = new ReconciliationEntry
            {
                ProviderMessageSid = message.Sid,
                Status = message.Status,
                DateSent = message.DateSent
            };
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                entry.NotificationId = local.Id;
                entry.OrderId = local.OrderId;
                entry.LocalStatus = local.Status;
                report.Matched.Add(entry);
            }
            else
            {
                report.ProviderOnly.Add(entry);
            }
        }

        foreach (var local in localNotifications)
        {
            if (local.ProviderMessageSid != null && !providerSids.Contains(local.ProviderMessageSid))
            {
                report.LocalOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageSid = local.ProviderMessageSid,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.Status
                });
            }
        }

        return report;
    }

    private async Task NotifyBuyerAsync(Order order, NotificationType type, string body, DateTimeOffset? sendAtUtc)
    {
        try
        {
            var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId));
            if (contactNumbers.Count == 0)
            {
                _logger.LogInformation("Order {OrderId}: buyer has no contact number on file; no {NotificationType} message sent",
                    order.Id, type);
                return;
            }

            foreach (var contactNumber in contactNumbers)
            {
                await SendAndRecordAsync(order.Id, order.BuyerId, contactNumber.PhoneNumber, type, body, sendAtUtc);
            }
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Order {OrderId}: failed to send {NotificationType} notification: {Error}", order.Id, type, ex.Message);
        }
    }

    private async Task<OrderNotification> SendAndRecordAsync(int orderId, string buyerId, string toNumber,
        NotificationType type, string body, DateTimeOffset? sendAtUtc, string? idempotencyKey = null)
    {
        OrderNotification notification;
        try
        {
            var message = await _messageProvider.SendMessageAsync(toNumber, body, sendAtUtc);
            notification = new OrderNotification(orderId, buyerId, toNumber, type, body,
                message.Sid, message.Status, sendAtUtc, idempotencyKey);
            notification.UpdateProviderState(message.Status, message.ErrorCode, message.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId}: provider rejected {NotificationType} notification: {Error}", orderId, type, ex.Message);
            notification = new OrderNotification(orderId, buyerId, toNumber, type, body,
                providerMessageSid: null, status: "failed", sendAtUtc, idempotencyKey);
            notification.UpdateProviderState("failed", null, ex.Message);
        }

        await _notificationRepository.AddAsync(notification);
        _logger.LogInformation("Order {OrderId}: recorded {NotificationType} notification {NotificationId} (provider message {MessageSid}, status {Status})",
            orderId, type, notification.Id, notification.ProviderMessageSid, notification.Status);
        return notification;
    }

    private async Task RefreshStatusAsync(OrderNotification notification)
    {
        if (notification.ProviderMessageSid == null || TerminalStatuses.Contains(notification.Status))
        {
            return;
        }

        try
        {
            var message = await _messageProvider.GetMessageAsync(notification.ProviderMessageSid);
            if (message != null &&
                (!string.Equals(message.Status, notification.Status, StringComparison.OrdinalIgnoreCase)
                 || message.ErrorCode != notification.ProviderErrorCode))
            {
                notification.UpdateProviderState(message.Status, message.ErrorCode, message.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh status for notification {NotificationId} (provider message {MessageSid}): {Error}",
                notification.Id, notification.ProviderMessageSid, ex.Message);
        }
    }
}
