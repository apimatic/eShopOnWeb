using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Sends the shopper-facing SMS notifications as an order moves and records each one.
/// Best-effort throughout: messaging failures are logged and recorded, never thrown,
/// so a notification problem can never fail the underlying order operation.
/// Never logs phone numbers or message bodies.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingService _messagingService;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingService messagingService,
        TwilioSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingService = messagingService;
        _settings = settings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        return NotifyAllNumbersSafely(order, NotificationKind.OrderPlaced, null,
            (number, token) =>
            {
                var body = $"eShopOnWeb: your order #{order.Id} has been placed (total {order.Total():C}). We'll text you when it ships.";
                return SendAsync(number.PhoneNumber, body, token);
            }, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        await NotifyAllNumbersSafely(order, NotificationKind.OrderDispatched, null,
            (number, token) =>
            {
                var body = $"eShopOnWeb: your order #{order.Id} is on its way!";
                return SendAsync(number.PhoneNumber, body, token);
            }, ct);

        // The follow-up is queued with the provider itself for a few days later —
        // nothing in this application holds it or sends it on a timer.
        var followUpAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        await NotifyAllNumbersSafely(order, NotificationKind.DeliveryFollowUp, followUpAt,
            (number, token) =>
            {
                var body = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? Reply and let us know.";
                return ScheduleAsync(number.PhoneNumber, body, followUpAt, token);
            }, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        await NotifyAllNumbersSafely(order, NotificationKind.OrderCancelled, null,
            (number, token) =>
            {
                var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. If you didn't request this, please contact support.";
                return SendAsync(number.PhoneNumber, body, token);
            }, ct);

        // A follow-up that has not yet gone out must never reach the shopper.
        await CancelScheduledFollowUpsForOrderAsync(order.Id, ct);
    }

    public async Task CancelScheduledMessagesToNumberAsync(string buyerId, string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var scheduled = await _notificationRepository.ListAsync(
                new ScheduledNotificationsToNumberSpecification(buyerId, phoneNumber), ct);
            foreach (var notification in scheduled)
            {
                await CancelScheduledNotificationAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to cancel scheduled messages for a removed contact number: {ex.Message}");
        }
    }

    public async Task RefreshDeliveryOutcomesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken ct = default)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.MessageSid))
            {
                continue;
            }

            // A canceled message is final; don't churn the provider for it.
            if (notification.Status == NotificationStatuses.Canceled)
            {
                continue;
            }

            try
            {
                var message = await _messagingService.GetMessageAsync(notification.MessageSid, ct);
                notification.UpdateDeliveryOutcome(message.Status ?? notification.Status,
                    message.ErrorCode, message.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not refresh delivery outcome for notification {notification.Id}: {ex.Message}");
            }
        }
    }

    private async Task<(ProviderMessage Message, string Body)> SendAsync(string to, string body, CancellationToken ct)
    {
        return (await _messagingService.SendMessageAsync(to, body, ct), body);
    }

    private async Task<(ProviderMessage Message, string Body)> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        return (await _messagingService.ScheduleMessageAsync(to, body, sendAt, ct), body);
    }

    private async Task CancelScheduledFollowUpsForOrderAsync(int orderId, CancellationToken ct)
    {
        try
        {
            var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
            foreach (var notification in notifications)
            {
                if (notification.Kind == NotificationKind.DeliveryFollowUp
                    && notification.Status == NotificationStatuses.Scheduled
                    && notification.MessageSid != null)
                {
                    await CancelScheduledNotificationAsync(notification, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to cancel scheduled follow-ups for order {orderId}: {ex.Message}");
        }
    }

    private async Task CancelScheduledNotificationAsync(OrderNotification notification, CancellationToken ct)
    {
        try
        {
            await _messagingService.CancelScheduledMessageAsync(notification.MessageSid!, ct);
            notification.UpdateDeliveryOutcome(NotificationStatuses.Canceled, null, null);
        }
        catch (MessagingException ex)
        {
            // The provider may reject the cancel because the message already went out;
            // re-read its actual state so the local record tells the truth either way.
            _logger.LogWarning($"Provider rejected cancel of scheduled message for notification {notification.Id}: {ex.Message}");
            try
            {
                var message = await _messagingService.GetMessageAsync(notification.MessageSid!, ct);
                notification.UpdateDeliveryOutcome(message.Status ?? notification.Status,
                    message.ErrorCode, message.ErrorMessage);
            }
            catch (Exception refreshEx)
            {
                _logger.LogWarning($"Could not refresh notification {notification.Id} after failed cancel: {refreshEx.Message}");
            }
        }

        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private async Task NotifyAllNumbersSafely(
        Order order,
        NotificationKind kind,
        DateTimeOffset? scheduledFor,
        Func<ContactNumber, CancellationToken, Task<(ProviderMessage Message, string Body)>> send,
        CancellationToken ct)
    {
        List<ContactNumber> numbers;
        try
        {
            numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not load contact numbers for order {order.Id} ({kind}): {ex.Message}");
            return;
        }

        // A shopper with no number on file is simply not messaged.
        foreach (var number in numbers)
        {
            try
            {
                var (message, body) = await send(number, ct);
                var notification = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber,
                    kind, body, message.Sid, message.Status ?? "unknown",
                    scheduledFor: scheduledFor, errorCode: message.ErrorCode, errorMessage: message.ErrorMessage);
                await _notificationRepository.AddAsync(notification, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send '{kind}' notification for order {order.Id}: {ex.Message}");
                await RecordFailedNotification(order, number, kind, ct);
            }
        }
    }

    private async Task RecordFailedNotification(Order order, ContactNumber number, NotificationKind kind, CancellationToken ct)
    {
        try
        {
            var failed = new OrderNotification(order.Id, order.BuyerId, number.Id, number.PhoneNumber,
                kind, null, null, NotificationStatuses.SendFailed, errorMessage: "The message could not be sent.");
            await _notificationRepository.AddAsync(failed, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not record failed notification for order {order.Id}: {ex.Message}");
        }
    }
}
