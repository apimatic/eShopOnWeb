using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Best-effort order SMS notifications. Provider failures are logged and swallowed so
/// they never fail the underlying order operation. Destination numbers are never logged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} was placed. Total: ${order.Total():0.00}. We'll text you when it's on its way.";
        await SendToBuyerAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news, your order #{order.Id} is on its way!";
        await SendToBuyerAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love to know.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleForBuyerAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToBuyerAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        // A follow-up that has not yet gone out must never reach a cancelled order's shopper.
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledNotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var notification in scheduled)
        {
            try
            {
                var state = await _smsService.CancelScheduledMessageAsync(notification.MessageSid, cancellationToken);
                notification.UpdateStatus(state.Status, state.ErrorCode, state.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled notification {NotificationId} (message sid {MessageSid}) for order {OrderId}",
                    notification.Id, notification.MessageSid, order.Id);
                notification.UpdateStatus(NotificationStatuses.Canceled, notification.ErrorCode,
                    "Cancellation requested at provider failed; treated as canceled locally.");
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
    }

    public async Task RefreshBuyerNotificationsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await RefreshAsync(notifications, cancellationToken);
    }

    private async Task RefreshAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (NotificationStatuses.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var state = await _smsService.GetMessageAsync(notification.MessageSid, cancellationToken);
                notification.UpdateStatus(state.Status, state.ErrorCode, state.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status of notification {NotificationId} (message sid {MessageSid}): {Error}",
                    notification.Id, notification.MessageSid, ex.Message);
            }
        }
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        foreach (var contactNumber in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            try
            {
                var result = await _smsService.SendMessageAsync(contactNumber.PhoneNumber, body, cancellationToken);
                await _notificationRepository.AddAsync(
                    new Notification(order.Id, order.BuyerId, contactNumber.PhoneNumber, type, body, result.MessageSid, result.Status),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send {NotificationType} notification for order {OrderId} to contact number {ContactNumberId}",
                    type, order.Id, contactNumber.Id);
            }
        }
    }

    private async Task ScheduleForBuyerAsync(Order order, NotificationType type, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        foreach (var contactNumber in await GetBuyerNumbersAsync(order.BuyerId, cancellationToken))
        {
            try
            {
                var result = await _smsService.ScheduleMessageAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
                await _notificationRepository.AddAsync(
                    new Notification(order.Id, order.BuyerId, contactNumber.PhoneNumber, type, body, result.MessageSid, result.Status, sendAt),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to schedule {NotificationType} notification for order {OrderId} to contact number {ContactNumberId}",
                    type, order.Id, contactNumber.Id);
            }
        }
    }

    private async Task<IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        // A shopper with no number on file is simply not messaged.
        return await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
    }
}
