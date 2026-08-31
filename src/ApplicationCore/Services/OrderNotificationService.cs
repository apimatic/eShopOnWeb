using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IMessagingService _messagingService;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<Order> orderRepository,
        IMessagingService messagingService,
        TwilioSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _orderRepository = orderRepository;
        _messagingService = messagingService;
        _settings = settings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: thank you for your order #{order.Id}! We'll text you when it's on its way.";
        return SendToShopperNumbersAsync(order, NotificationType.OrderPlaced, body, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"eShopOnWeb: good news - your order #{order.Id} has been dispatched and is on its way.";
        await SendToShopperNumbersAsync(order, NotificationType.OrderDispatched, body, ct);

        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayInDays);
        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now - how did the delivery go?";
        await ScheduleFollowUpsAsync(order, followUpBody, sendAt, ct);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        // Call off any provider-queued follow-up first: a customer whose order was cancelled
        // must never later be asked how the delivery went.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperNumbersAsync(order, NotificationType.OrderCancelled, body, ct);
    }

    public async Task CancelPendingMessagesToNumberAsync(string buyerId, string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var pending = await _notificationRepository.ListAsync(
                new PendingNotificationsToNumberSpecification(buyerId, phoneNumber), ct);
            foreach (var notification in pending)
            {
                await CancelScheduledAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel pending messages for buyer {BuyerId}.", buyerId);
        }
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default)
    {
        if (notification.MessageSid is null || notification.IsTerminal)
        {
            return;
        }

        try
        {
            var message = await _messagingService.GetMessageAsync(notification.MessageSid, ct);
            if (message is null)
            {
                return;
            }

            notification.UpdateProviderStatus(message.Status, message.ErrorCode, message.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh status for notification {NotificationId}; returning last known state.", notification.Id);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        foreach (var notification in notifications)
        {
            await RefreshStatusAsync(notification, ct);
        }
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(OrderNotification source, string idempotencyKey, CancellationToken ct = default)    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            _logger.LogInformation("Resend under an already-used idempotency key; returning notification {NotificationId} without sending again.", existing.Id);
            return new ResendResult(existing, true);
        }

        var resend = new OrderNotification(source.OrderId, source.BuyerId, source.RecipientNumber,
            NotificationType.Resend, source.Body!, idempotencyKey: idempotencyKey,
            resendOfNotificationId: source.Id);

        var outcome = await _messagingService.SendMessageAsync(source.RecipientNumber, source.Body!, ct);
        ApplyOutcome(resend, outcome);

        await _notificationRepository.AddAsync(resend, ct);
        return new ResendResult(resend, false);
    }

    public async Task<bool> RedactContentAsync(OrderNotification notification, CancellationToken ct = default)
    {
        if (notification.ContentRedacted)
        {
            return true;
        }

        if (notification.MessageSid is not null)
        {
            var outcome = await _messagingService.RedactMessageBodyAsync(notification.MessageSid, ct);
            if (!outcome.Success)
            {
                _logger.LogWarning("Provider could not redact message for notification {NotificationId} (HTTP {Status}, provider code {Code}).",
                    notification.Id, outcome.ProviderStatusCode?.ToString() ?? "none", outcome.ProviderErrorCode?.ToString() ?? "none");
                return false;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        return true;
    }

    private async Task SendToShopperNumbersAsync(Order order, NotificationType type, string body, CancellationToken ct)
    {
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
            foreach (var number in numbers)
            {
                var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, type, body);
                var outcome = await _messagingService.SendMessageAsync(number.PhoneNumber, body, ct);
                ApplyOutcome(notification, outcome);
                await _notificationRepository.AddAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            // A messaging failure must never fail the underlying operation.
            _logger.LogError(ex, "Failed to notify buyer {BuyerId} about order {OrderId} ({Type}).", order.BuyerId, order.Id, type);
        }
    }

    private async Task ScheduleFollowUpsAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(
                new ContactNumbersByBuyerSpecification(order.BuyerId), ct);
            foreach (var number in numbers)
            {
                var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber,
                    NotificationType.DeliveryFollowUp, body, scheduledFor: sendAt);
                var outcome = await _messagingService.ScheduleMessageAsync(number.PhoneNumber, body, sendAt, ct);
                ApplyOutcome(notification, outcome);
                await _notificationRepository.AddAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule delivery follow-up for order {OrderId}.", order.Id);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        try
        {
            var pending = await _notificationRepository.ListAsync(
                new PendingFollowUpsForOrderSpecification(orderId), ct);
            foreach (var notification in pending)
            {
                await CancelScheduledAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel pending follow-ups for order {OrderId}.", orderId);
        }
    }

    private async Task CancelScheduledAsync(OrderNotification notification, CancellationToken ct)
    {
        if (notification.MessageSid is null)
        {
            // Never reached the provider; nothing queued there to cancel.
            notification.UpdateProviderStatus(NotificationStatuses.Canceled, notification.ErrorCode, notification.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, ct);
            return;
        }

        // A freshly scheduled message can briefly reject the cancel with a 404 while the
        // provider's record propagates (GET succeeds, POST update 404s). Retry through that
        // window - a follow-up for a cancelled order must never reach the shopper.
        MessagingOutcome outcome;
        var attempt = 0;
        while (true)
        {
            outcome = await _messagingService.CancelScheduledMessageAsync(notification.MessageSid, ct);
            attempt++;
            if (outcome.Success || outcome.FailureKind != MessagingFailureKind.Rejected
                || outcome.ProviderStatusCode != 404 || attempt >= MaxCancelAttempts)
            {
                break;
            }

            _logger.LogInformation("Cancel of scheduled message for notification {NotificationId} not yet visible to the provider (attempt {Attempt}); retrying.",
                notification.Id, attempt);
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }

        if (outcome.Success)
        {
            notification.UpdateProviderStatus(outcome.Status ?? NotificationStatuses.Canceled, null, null);
        }
        else
        {
            // The provider may already have sent it; settle from the provider's own record.
            _logger.LogWarning("Could not cancel scheduled message {MessageSid} for notification {NotificationId} (HTTP {Status}, provider code {Code}).",
                notification.MessageSid, notification.Id, outcome.ProviderStatusCode?.ToString() ?? "none", outcome.ProviderErrorCode?.ToString() ?? "none");
            var current = await _messagingService.GetMessageAsync(notification.MessageSid, ct);
            if (current is not null)
            {
                notification.UpdateProviderStatus(current.Status, current.ErrorCode, current.ErrorMessage);
            }
        }
        await _notificationRepository.UpdateAsync(notification, ct);
    }

    private const int MaxCancelAttempts = 6;

    private void ApplyOutcome(OrderNotification notification, MessagingOutcome outcome)
    {
        if (outcome.Success && outcome.MessageSid is not null)
        {
            notification.RecordAccepted(outcome.MessageSid, outcome.Status ?? NotificationStatuses.Queued);
        }
        else
        {
            notification.RecordSendFailure(outcome.ProviderErrorCode, outcome.ProviderErrorMessage);
            _logger.LogWarning("Message for order {OrderId} ({Type}) was not accepted by the provider (HTTP {Status}, provider code {Code}).",
                notification.OrderId, notification.Type, outcome.ProviderStatusCode?.ToString() ?? "none", outcome.ProviderErrorCode?.ToString() ?? "none");
        }
    }
}
