using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider statuses that leave nothing to poll for.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled"
    };

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        IRepository<Order> orderRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _orderRepository = orderRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed (total ${order.Total():0.00}). We'll text you when it's on its way.";
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShop: good news — order #{order.Id} is on its way.";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, ct);

        var followUpBody = $"eShop: order #{order.Id} should have reached you by now — how did the delivery go?";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleForShopperAsync(order, followUpBody, sendAt, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        // Call off any follow-up that has not yet gone out — a cancelled order must never
        // produce a "how did the delivery go" message.
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id), ct);
        var pendingFollowUps = notifications
            .Where(n => n.Type == NotificationType.DeliveryFollowUp
                        && n.MessageSid != null
                        && string.Equals(n.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var state = await _smsGateway.CancelScheduledAsync(followUp.MessageSid!, ct);
                followUp.UpdateProviderState(state.Status ?? "canceled", state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(followUp, ct);
            }
            catch (Exception ex)
            {
                // Never fail the cancel operation over a provider problem; the follow-up's
                // recorded state stays "scheduled" so the discrepancy is visible.
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}: {ExceptionType}.",
                    followUp.Id, order.Id, ex.GetType().Name);
            }
        }

        var body = $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, ct);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
    }

    public async Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new NotFoundException("Order not found.");
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);

        // No provider callbacks exist (no public URL), so refresh non-terminal outcomes by asking
        // the provider — best-effort: a provider hiccup must not break the read.
        foreach (var notification in notifications)
        {
            if (notification.MessageSid == null || TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _smsGateway.GetStateAsync(notification.MessageSid, ct);
                notification.UpdateProviderState(state.Status ?? notification.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh provider state for notification {NotificationId}: {ExceptionType}.",
                    notification.Id, ex.GetType().Name);
            }
        }

        return notifications;
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body, CancellationToken ct)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new Notification(order.Id, order.BuyerId, contactNumber.PhoneNumber, type, body);
            await _notificationRepository.AddAsync(notification, ct);

            try
            {
                var result = await _smsGateway.SendAsync(contactNumber.PhoneNumber, body, ct);
                RecordSendOutcome(notification, result);
            }
            catch (Exception ex)
            {
                RecordSendException(notification, ex);
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    private async Task ScheduleForShopperAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new Notification(order.Id, order.BuyerId, contactNumber.PhoneNumber,
                NotificationType.DeliveryFollowUp, body, scheduledFor: sendAt);
            await _notificationRepository.AddAsync(notification, ct);

            try
            {
                var result = await _smsGateway.ScheduleAsync(contactNumber.PhoneNumber, body, sendAt, ct);
                RecordSendOutcome(notification, result);
            }
            catch (Exception ex)
            {
                RecordSendException(notification, ex);
            }

            await _notificationRepository.UpdateAsync(notification, ct);
        }
    }

    private void RecordSendOutcome(Notification notification, SmsSendResult result)
    {
        if (result.MessageSid != null)
        {
            notification.MarkSent(result.MessageSid, result.Status ?? "queued");
        }
        else
        {
            notification.MarkSendFailed(result.ErrorMessage ?? "The provider did not return a message identifier.");
        }
    }

    private void RecordSendException(Notification notification, Exception ex)
    {
        if (ex is SmsProviderException { OutcomeUnknown: true })
        {
            notification.MarkSendOutcomeUnknown("The send may have reached the provider before the connection failed.");
        }
        else
        {
            notification.MarkSendFailed("The message could not be sent.");
        }

        // Never log the destination number; the notification id is enough to trace.
        _logger.LogWarning("Failed to send notification {NotificationId} for order {OrderId}: {ExceptionType}.",
            notification.Id, notification.OrderId, ex.GetType().Name);
    }
}
