using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How long after dispatch the delivery follow-up is queued with the provider for.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsGateway smsGateway,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Your order #{order.Id} has been placed. Total: USD {order.Total():0.00}. Thank you for shopping with us!";
        await SendToBuyerAsync(order, NotificationType.OrderPlaced, body, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"eShopOnWeb: Good news! Your order #{order.Id} is on its way.";
        await SendToBuyerAsync(order, NotificationType.OrderDispatched, dispatchedBody, null, cancellationToken);

        // The follow-up is scheduled with the provider itself, not held in this app.
        var followUpBody = $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love to hear from you.";
        await SendToBuyerAsync(order, NotificationType.DeliveryFollowUp, followUpBody,
            DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A follow-up that has not yet gone out must never reach a cancelled order's shopper.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledNotificationsByOrderSpecification(order.Id), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelScheduledAsync(notification, cancellationToken);
        }

        var body = $"eShopOnWeb: Your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToBuyerAsync(order, NotificationType.OrderCancelled, body, null, cancellationToken);
    }

    public async Task CancelScheduledForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        var scheduled = await _notificationRepository.ListAsync(new ScheduledNotificationsByContactNumberSpecification(contactNumberId), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelScheduledAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.MessageSid is null)
        {
            return;
        }

        try
        {
            var status = await _smsGateway.GetMessageStatusAsync(notification.MessageSid, cancellationToken);
            if (status is not null && status.Status != notification.Status)
            {
                notification.UpdateStatus(status.Status, status.ErrorCode, status.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not refresh status for notification {notification.Id}: {ex.Message}");
        }
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResult(ResendOutcome.AlreadyProcessed, existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResult(ResendOutcome.NotFound, null);
        }

        string destination;
        if (original.ContactNumberId is int contactNumberId)
        {
            var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
            if (contactNumber is null)
            {
                // The number was removed; nothing may be sent to it again.
                return new ResendResult(ResendOutcome.DestinationNoLongerRegistered, null);
            }
            destination = contactNumber.PhoneNumber;
        }
        else
        {
            return new ResendResult(ResendOutcome.DestinationNoLongerRegistered, null);
        }

        var body = original.Body ?? string.Empty;
        var notification = await SendAsync(original.OrderId, original.BuyerId, original.ContactNumberId,
            original.NotificationType, body, null, idempotencyKey, cancellationToken);

        return new ResendResult(ResendOutcome.Completed, notification);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.MessageSid is not null && !notification.IsContentRedacted)
        {
            try
            {
                await _smsGateway.RedactMessageBodyAsync(notification.MessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to redact message content at provider for notification {notification.Id}: {ex.Message}");
                throw;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body,
        DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        List<ContactNumber> contactNumbers;
        try
        {
            contactNumbers = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Could not load contact numbers for order {order.Id}: {ex.Message}");
            return;
        }

        foreach (var contactNumber in contactNumbers)
        {
            await SendAsync(order.Id, order.BuyerId, contactNumber.Id, type, body, sendAtUtc, null, cancellationToken);
        }
    }

    private async Task<OrderNotification> SendAsync(int orderId, string buyerId, int? contactNumberId,
        NotificationType type, string body, DateTimeOffset? sendAtUtc, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // The destination number itself is deliberately never logged.
        SmsSendResult result;
        string destination;
        try
        {
            destination = contactNumberId is int id
                ? (await _contactNumberRepository.GetByIdAsync(id, cancellationToken))?.PhoneNumber ?? string.Empty
                : string.Empty;

            if (string.IsNullOrEmpty(destination))
            {
                result = new SmsSendResult(false, null, null, null, "Destination number is no longer registered.");
            }
            else
            {
                result = await _smsGateway.SendAsync(destination, body, sendAtUtc, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"SMS send for order {orderId} ({type}) threw: {ex.Message}");
            result = new SmsSendResult(false, null, null, null, ex.Message);
        }

        var notification = new OrderNotification(orderId, buyerId, contactNumberId, type, body,
            result.MessageSid,
            result.Success ? result.Status ?? "queued" : OrderNotification.LocalFailedStatus,
            result.Success ? sendAtUtc : null,
            idempotencyKey,
            result.ErrorCode, result.ErrorMessage);

        try
        {
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Could not record notification for order {orderId}: {ex.Message}");
        }

        return notification;
    }

    private async Task CancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var cancelled = await _smsGateway.CancelScheduledMessageAsync(notification.MessageSid!, cancellationToken);
            notification.UpdateStatus(cancelled ? "canceled" : notification.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to cancel scheduled message for notification {notification.Id}: {ex.Message}");
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }
}
