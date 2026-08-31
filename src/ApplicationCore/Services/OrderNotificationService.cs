using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Best-effort order SMS notifications. Every provider interaction is isolated so that a
/// messaging failure never fails the underlying order operation. Destination phone numbers
/// and credentials are never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsNotificationClient _smsClient;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationClient smsClient,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsClient = smsClient;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Total: ${order.Total():0.00}. Thank you for shopping with us!";
        await SendToBuyerAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        if (numbers.Count == 0)
        {
            return;
        }

        var dispatchBody = $"eShop: good news! Your order #{order.Id} has been dispatched and is on its way.";
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);

        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, number, NotificationType.OrderDispatched, dispatchBody, null, cancellationToken);
            await ScheduleAndRecordAsync(order, number, followUpBody, followUpAt, cancellationToken);
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToBuyerAsync(order, NotificationType.OrderCancelled, body, cancellationToken);
    }

    public async Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications.Where(n => !string.IsNullOrEmpty(n.MessageSid) && !NotificationStatus.IsTerminal(n.Status)))
        {
            try
            {
                var details = await _smsClient.GetMessageAsync(notification.MessageSid!, cancellationToken);
                notification.UpdateStatus(details.Status, details.ErrorCode, details.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId} (order {OrderId}): {ErrorType}", notification.Id, orderId, ex.GetType().Name);
            }
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        var pendingFollowUps = notifications
            .Where(n => n.Type == NotificationType.DeliveryFollowUp
                        && !string.IsNullOrEmpty(n.MessageSid)
                        && !NotificationStatus.IsTerminal(n.Status))
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var result = await _smsClient.CancelScheduledMessageAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateStatus(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up notification {NotificationId} (order {OrderId}): {ErrorType}", followUp.Id, orderId, ex.GetType().Name);
            }
        }
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            await SendAndRecordAsync(order, number, type, body, null, cancellationToken);
        }
    }

    private async Task<System.Collections.Generic.IReadOnlyList<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            _logger.LogInformation("Buyer {BuyerId} has no contact number on file; skipping notification.", buyerId);
        }
        return numbers;
    }

    private async Task SendAndRecordAsync(Order order, ContactNumber number, NotificationType type, string body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, type, body, null, idempotencyKey);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsClient.SendMessageAsync(number.PhoneNumber, body, cancellationToken);
            notification.MarkSubmitted(result.MessageSid!, result.Status);
        }
        catch (Exception ex)
        {
            notification.MarkFailed(NotificationStatus.Failed, null, ex.GetType().Name);
            _logger.LogWarning("Failed to send {NotificationType} notification {NotificationId} for order {OrderId}: {ErrorType}", type, notification.Id, order.Id, ex.GetType().Name);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task ScheduleAndRecordAsync(Order order, ContactNumber number, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, NotificationType.DeliveryFollowUp, body, sendAt);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _smsClient.ScheduleMessageAsync(number.PhoneNumber, body, sendAt, cancellationToken);
            notification.MarkSubmitted(result.MessageSid!, result.Status);
        }
        catch (Exception ex)
        {
            notification.MarkFailed(NotificationStatus.Failed, null, ex.GetType().Name);
            _logger.LogWarning("Failed to schedule follow-up notification {NotificationId} for order {OrderId}: {ErrorType}", notification.Id, order.Id, ex.GetType().Name);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }
}
