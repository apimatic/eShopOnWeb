using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends order notifications as an order moves. Messaging must never fail the underlying
/// operation: every provider interaction is best-effort and its outcome is recorded.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up is queued with the provider for a few days after dispatch.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Thank you! Your order #{order.Id} has been placed. Total: ${order.Total():0.00}.";
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Good news! Your order #{order.Id} is on its way.";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await ScheduleForShopperAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper of a cancelled order.
        var pendingFollowUps = await _notificationRepository.ListAsync(new PendingFollowUpsForOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                // The provider can briefly refuse to cancel a message that was scheduled
                // moments ago; retry with backoff before giving up.
                var cancelled = false;
                for (var attempt = 0; attempt < 4 && !cancelled; attempt++)
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                    }
                    cancelled = await _smsService.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                }

                if (cancelled)
                {
                    followUp.UpdateProviderStatus("canceled", null, null);
                }
                else
                {
                    await RefreshStatusAsync(followUp, cancellationToken);
                    if (followUp.Status == "scheduled")
                    {
                        _logger.LogWarning("Scheduled follow-up notification {NotificationId} for order {OrderId} could not be canceled at the provider.", followUp.Id, order.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}: {ExceptionType}", followUp.Id, order.Id, ex.GetType().Name);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.ProviderMessageSid == null || notification.IsTerminal)
            {
                continue;
            }

            await RefreshStatusAsync(notification, cancellationToken);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task<OrderNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        return await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
    }

    private async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _smsService.GetMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            if (state?.Status != null)
            {
                notification.UpdateProviderStatus(state.Status, state.ErrorCode, state.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh status for notification {NotificationId}: {ExceptionType}", notification.Id, ex.GetType().Name);
        }
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var contactNumber = await GetShopperContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber == null)
        {
            return; // A shopper with no number on file is simply not messaged.
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber, type, body);
        await TrySendAsync(notification, body, null, cancellationToken);
    }

    private async Task ScheduleForShopperAsync(Order order, NotificationType type, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var contactNumber = await GetShopperContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber == null)
        {
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber, type, body, scheduledFor: sendAt);

        try
        {
            var result = await _smsService.ScheduleMessageAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
            ApplyResult(notification, result, sendAt);
        }
        catch (Exception ex)
        {
            notification.MarkFailed("The messaging provider could not be reached.");
            _logger.LogWarning("Failed to schedule notification for order {OrderId}: {ExceptionType}", order.Id, ex.GetType().Name);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task TrySendAsync(OrderNotification notification, string body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsService.SendMessageAsync(notification.ToNumber, body, cancellationToken);
            ApplyResult(notification, result, null);
        }
        catch (Exception ex)
        {
            notification.MarkFailed("The messaging provider could not be reached.");
            _logger.LogWarning("Failed to send notification for order {OrderId}: {ExceptionType}", notification.OrderId, ex.GetType().Name);
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private static void ApplyResult(OrderNotification notification, SmsSendResult result, DateTimeOffset? scheduledFor)
    {
        if (result.Accepted && result.ProviderMessageSid != null)
        {
            notification.MarkProviderAccepted(result.ProviderMessageSid, result.Status ?? "queued", scheduledFor);
        }
        else
        {
            notification.MarkFailed(result.ErrorMessage ?? "The messaging provider rejected the message.", result.ErrorCode);
        }
    }

    private async Task<ContactNumber?> GetShopperContactNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        // The shopper's most recently registered number is the one we message.
        return await _contactNumberRepository.FirstOrDefaultAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }
}
