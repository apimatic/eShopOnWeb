using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How long after dispatch the provider should hold the delivery follow-up before sending it.
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsProvider smsProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = string.Format(CultureInfo.InvariantCulture,
            "Your eShop order #{0} has been placed (total ${1:0.00}). We'll text you when it's on its way.",
            order.Id, order.Total());
        return NotifyAsync(order, OrderNotificationType.OrderPlaced, body, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"Good news! Your eShop order #{order.Id} has been dispatched and is on its way.";
        await NotifyAsync(order, OrderNotificationType.OrderDispatched, body, null, cancellationToken);

        var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await NotifyAsync(order, OrderNotificationType.DeliveryFollowUp, followUpBody, sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"Your eShop order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await NotifyAsync(order, OrderNotificationType.OrderCancelled, body, null, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper.
        var scheduledSpec = new ScheduledFollowUpsByOrderSpecification(order.Id);
        var scheduledFollowUps = await _notificationRepository.ListAsync(scheduledSpec, cancellationToken);
        foreach (var followUp in scheduledFollowUps)
        {
            try
            {
                var cancelled = await _smsProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateProviderStatus(cancelled ? "canceled" : followUp.Status, followUp.ProviderErrorCode);
                await _notificationRepository.UpdateAsync(followUp, cancellationToken);
                if (!cancelled)
                {
                    _logger.LogWarning($"Could not cancel scheduled follow-up notification {followUp.Id} for order {order.Id} at the provider.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to cancel scheduled follow-up notification {followUp.Id} for order {order.Id}: {ex.GetType().Name}");
            }
        }
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existingSpec = new NotificationByIdempotencyKeySpecification(idempotencyKey);
        var existing = await _notificationRepository.FirstOrDefaultAsync(existingSpec, cancellationToken);
        if (existing is not null)
        {
            return new ResendNotificationResult { Outcome = ResendNotificationOutcome.Duplicate, Notification = existing };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendNotificationResult { Outcome = ResendNotificationOutcome.NotificationNotFound };
        }

        if (original.ContentDisposed || original.Body is null)
        {
            return new ResendNotificationResult { Outcome = ResendNotificationOutcome.ContentDisposed };
        }

        // Nothing may be sent to a number the shopper has since removed.
        var contactSpec = new ContactNumbersByOwnerSpecification(original.OwnerId);
        var registeredNumbers = await _contactNumberRepository.ListAsync(contactSpec, cancellationToken);
        if (!registeredNumbers.Any(c => c.PhoneNumber == original.ToNumber))
        {
            return new ResendNotificationResult { Outcome = ResendNotificationOutcome.DestinationNoLongerRegistered };
        }

        var resend = new OrderNotification(original.OrderId, original.OwnerId, original.Type, original.ToNumber,
            original.Body, scheduledFor: null, resendOfNotificationId: original.Id, idempotencyKey: idempotencyKey);

        await SendAndRecordAsync(resend, null, cancellationToken);

        return new ResendNotificationResult { Outcome = ResendNotificationOutcome.Sent, Notification = resend };
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ProviderMessageSid is null || OrderNotification.IsTerminalStatus(notification.Status))
        {
            return;
        }

        try
        {
            var details = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (details?.Status is not null && details.Status != notification.Status)
            {
                notification.UpdateProviderStatus(details.Status, details.ErrorCode);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // A status refresh must never break the read that triggered it.
            _logger.LogWarning($"Failed to refresh status of notification {notification.Id}: {ex.GetType().Name}");
        }
    }

    private async Task NotifyAsync(Order order, OrderNotificationType type, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var contactNumber = await GetCurrentContactNumberAsync(order.BuyerId, cancellationToken);
            if (contactNumber is null)
            {
                // A shopper with no number on file is simply not messaged.
                return;
            }

            var notification = new OrderNotification(order.Id, order.BuyerId, type, contactNumber.PhoneNumber, body, sendAt);
            await SendAndRecordAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning($"Failed to send {type} notification for order {order.Id}: {ex.GetType().Name}");
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var result = await _smsProvider.SendMessageAsync(notification.ToNumber, notification.Body!, sendAt, cancellationToken);
        if (result.Accepted && result.MessageSid is not null)
        {
            notification.MarkSent(result.MessageSid, result.Status ?? (sendAt.HasValue ? "scheduled" : "queued"));
        }
        else
        {
            notification.MarkSendFailed(result.ErrorCode);
            _logger.LogWarning($"Provider rejected notification for order {notification.OrderId}: {result.ErrorCode} {result.ErrorMessage}");
        }

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task<ContactNumber?> GetCurrentContactNumberAsync(string ownerId, CancellationToken cancellationToken)
    {
        var spec = new ContactNumbersByOwnerSpecification(ownerId);
        var numbers = await _contactNumberRepository.ListAsync(spec, cancellationToken);
        return numbers.FirstOrDefault();
    }
}
