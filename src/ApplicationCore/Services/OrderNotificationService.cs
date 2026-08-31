using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessageProvider _messageProvider;
    private readonly NotificationSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IMessageProvider messageProvider,
        NotificationSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messageProvider = messageProvider;
        _settings = settings;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. " +
                   $"Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you for shopping with us!";
        await SendToBuyerAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news! Your order #{order.Id} is on its way.";
        await SendToBuyerAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        // Queue the delivery follow-up with the provider itself; this app holds no timer.
        var sendAt = DateTimeOffset.UtcNow.Add(_settings.FollowUpDelay);
        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        await ScheduleForBuyerAsync(order, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToBuyerAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        // A follow-up that has not yet gone out must never reach a cancelled order's customer.
        var scheduled = await _notificationRepository.ListAsync(new ScheduledOrderNotificationsSpecification(order.Id), cancellationToken);
        foreach (var followUp in scheduled)
        {
            try
            {
                var cancelled = await _messageProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (cancelled)
                {
                    followUp.MarkCanceled();
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
                else
                {
                    _logger.LogWarning($"Could not cancel scheduled follow-up notification {followUp.Id} for order {order.Id} at the provider.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cancelling scheduled follow-up notification {followUp.Id} for order {order.Id}: {ex.GetType().Name}");
            }
        }
    }

    public async Task<ResendResponse> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendResponse { Result = ResendResult.NotFound };
        }

        // Repeating a request under the same key must not send a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return new ResendResponse { Result = ResendResult.AlreadyProcessed, Notification = existing };
        }

        // A deleted contact number must never be sent to again.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber is null)
        {
            return new ResendResponse { Result = ResendResult.DestinationRemoved };
        }

        if (original.ContentRedacted)
        {
            return new ResendResponse { Result = ResendResult.ContentRedacted };
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            original.ToNumber, original.Type, original.Body,
            idempotencyKey: idempotencyKey, resendOfNotificationId: original.Id);
        resend = await _notificationRepository.AddAsync(resend, cancellationToken);

        await SendAndRecordAsync(resend, cancellationToken);

        return new ResendResponse { Result = ResendResult.Sent, Notification = resend };
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentRedacted)
        {
            return true;
        }

        // Redact at the provider too, so the text is not merely hidden by this application.
        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _messageProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error redacting notification {notification.Id} at the provider: {ex.GetType().Name}");
                throw;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.IsTerminal || string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messageProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
                if (current is not null && current.Status is not null && current.Status != notification.ProviderStatus)
                {
                    notification.UpdateProviderStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not refresh status of notification {notification.Id}: {ex.GetType().Name}");
            }
        }
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var contactNumber = await GetPrimaryContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber is null)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber, type, body);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);

        await SendAndRecordAsync(notification, cancellationToken);
    }

    private async Task ScheduleForBuyerAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var contactNumber = await GetPrimaryContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber is null)
        {
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber,
            NotificationType.DeliveryFollowUp, body, scheduledFor: sendAt);
        notification = await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var result = await _messageProvider.ScheduleMessageAsync(notification.ToNumber, notification.Body, sendAt, cancellationToken);
            if (result.Accepted)
            {
                notification.MarkAccepted(result.MessageSid!, result.Status ?? "scheduled");
            }
            else
            {
                notification.MarkRejected(result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to schedule follow-up notification {notification.Id} for order {order.Id}: {ex.GetType().Name}");
            notification.MarkRejected("client-error", "The messaging provider could not be reached.");
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task SendAndRecordAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messageProvider.SendMessageAsync(notification.ToNumber, notification.Body, cancellationToken);
            if (result.Accepted)
            {
                notification.MarkAccepted(result.MessageSid!, result.Status ?? "queued");
            }
            else
            {
                notification.MarkRejected(result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogError($"Failed to send notification {notification.Id} for order {notification.OrderId}: {ex.GetType().Name}");
            notification.MarkRejected("client-error", "The messaging provider could not be reached.");
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task<ContactNumber?> GetPrimaryContactNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.FirstOrDefault();
    }
}
