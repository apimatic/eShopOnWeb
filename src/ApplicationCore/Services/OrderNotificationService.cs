using System;
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
    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly NotificationSettings _settings;

    public OrderNotificationService(
        ISmsProvider smsProvider,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IAppLogger<OrderNotificationService> logger,
        NotificationSettings settings)
    {
        _smsProvider = smsProvider;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _logger = logger;
        _settings = settings;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        return NotifyAsync(order, NotificationType.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!",
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(order, NotificationType.OrderDispatched,
            $"eShop: good news - order #{order.Id} is on its way!",
            cancellationToken);

        // The follow-up is queued with the provider itself (scheduled send), not held
        // in this application, so it goes out even if this app is not running.
        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        await NotifyAsync(order, NotificationType.DeliveryFollowUp,
            $"eShop: how did the delivery of order #{order.Id} go? We'd love to hear from you.",
            cancellationToken, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAsync(order, NotificationType.OrderCancelled,
            $"eShop: your order #{order.Id} has been cancelled. Contact support if this is unexpected.",
            cancellationToken);

        // A follow-up that has not gone out yet must never reach the shopper of a
        // cancelled order - cancel it at the provider.
        try
        {
            var pendingFollowUps = await _notificationRepository.ListAsync(
                new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);

            foreach (var followUp in pendingFollowUps)
            {
                try
                {
                    var cancelled = await _smsProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                    followUp.UpdateProviderStatus(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
                    await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cancel scheduled follow-up notification {NotificationId} for order {OrderId}", followUp.Id, order.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel scheduled follow-ups for order {OrderId}", order.Id);
        }
    }

    public async Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new ResendByIdempotencyKeySpecification(original.Id, idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // A deleted contact number must never be messaged again.
        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != original.BuyerId)
        {
            throw new InvalidOperationException("The contact number for this notification is no longer registered; it must not be messaged again.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            NotificationType.Resend, original.Body, idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        try
        {
            var sent = await _smsProvider.SendMessageAsync(contactNumber.PhoneNumber, original.Body ?? string.Empty, cancellationToken);
            resend.MarkAccepted(sent.Sid, sent.Status);
        }
        catch (SmsProviderException ex)
        {
            resend.MarkRejected("failed", ex.ProviderErrorCode, ex.Message);
        }

        await _notificationRepository.AddAsync(resend, cancellationToken);
        return resend;
    }

    public async Task RefreshProviderStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (!notification.AcceptedByProvider || notification.ProviderMessageSid is null)
        {
            return;
        }

        try
        {
            var message = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderStatus(message.Status, message.ErrorCode, message.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh provider status for notification {NotificationId}", notification.Id);
        }
    }

    private async Task NotifyAsync(Order order, NotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduledFor = null)
    {
        try
        {
            var contactNumbers = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            var contactNumber = contactNumbers.FirstOrDefault();

            // A shopper with no number on file is simply not messaged.
            if (contactNumber is null)
            {
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body, scheduledFor);

            try
            {
                ProviderMessage result = scheduledFor.HasValue
                    ? await _smsProvider.ScheduleMessageAsync(contactNumber.PhoneNumber, body, scheduledFor.Value, cancellationToken)
                    : await _smsProvider.SendMessageAsync(contactNumber.PhoneNumber, body, cancellationToken);
                notification.MarkAccepted(result.Sid, result.Status);
            }
            catch (SmsProviderException ex)
            {
                notification.MarkRejected("failed", ex.ProviderErrorCode, ex.Message);
                _logger.LogError(ex, "Provider rejected {NotificationType} notification for order {OrderId}", type, order.Id);
            }

            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogError(ex, "Failed to send {NotificationType} notification for order {OrderId}", type, order.Id);
        }
    }
}
