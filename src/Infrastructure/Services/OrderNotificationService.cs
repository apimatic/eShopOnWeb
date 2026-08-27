using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates order SMS notifications. Every provider interaction is best-effort:
/// a message that cannot be sent is recorded and never fails the order operation.
/// Destination numbers and message bodies are never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How long after dispatch the provider should send the delivery follow-up.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", OrderNotification.LocalSendFailedStatus
    };

    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsMessagingClient _messagingClient;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsMessagingClient messagingClient,
        ILogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
        => NotifyAllAsync(order, OrderNotificationType.OrderPlaced,
            $"eShopOnWeb: thanks for your order #{order.Id}! We'll text you when it's on its way.",
            null, cancellationToken);

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAllAsync(order, OrderNotificationType.OrderDispatched,
            $"eShopOnWeb: good news — your order #{order.Id} has been dispatched and is on its way!",
            null, cancellationToken);

        // The follow-up is queued with the provider itself (scheduled send), not held in-app.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyAllAsync(order, OrderNotificationType.DeliveryFollowUp,
            $"eShopOnWeb: your order #{order.Id} should have arrived by now — how did the delivery go?",
            sendAt, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifyAllAsync(order, OrderNotificationType.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
            null, cancellationToken);

        // A follow-up that has not yet gone out must never reach a cancelled order's shopper.
        var pendingFollowUps = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            if (followUp.ProviderMessageSid is null)
            {
                continue;
            }
            try
            {
                var result = await _messagingClient.CancelScheduledMessageAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.UpdateProviderStatus(result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel scheduled follow-up {MessageSid} at the provider", followUp.ProviderMessageSid);
                followUp.UpdateProviderStatus(followUp.Status, "Failed to cancel the scheduled follow-up at the provider.");
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            // Same key seen before: return the message the first attempt produced, send nothing.
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return null;
        }
        if (original.ContentRedacted || original.Body is null)
        {
            throw new InvalidOperationException("The content of this notification has been disposed of and can no longer be re-sent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            OrderNotificationType.Resend, original.Body, idempotencyKey);
        await SendAndRecordAsync(resend, null, cancellationToken);
        return resend;
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        if (notification.ProviderMessageSid is not null)
        {
            // Redact at the provider first: the text must stop being retrievable there, not just here.
            await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ProviderMessageSid is null || TerminalStatuses.Contains(notification.Status))
        {
            return;
        }

        try
        {
            var result = await _messagingClient.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateProviderStatus(result.Status, result.ErrorMessage);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh status for message {MessageSid}", notification.ProviderMessageSid);
        }
    }

    private async Task NotifyAllAsync(Order order, OrderNotificationType type, string body, DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        List<ContactNumber> numbers;
        try
        {
            numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load contact numbers for order {OrderId}; skipping {Type} notification", order.Id, type);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber, type, body,
                scheduledForUtc: sendAtUtc);
            await SendAndRecordAsync(notification, sendAtUtc, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, DateTimeOffset? sendAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _messagingClient.SendMessageAsync(notification.ToNumber, notification.Body!, sendAtUtc, cancellationToken);
            notification.MarkAccepted(result.ProviderMessageSid, result.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning(ex, "Notification {Type} for order {OrderId} could not be handed to the provider", notification.Type, notification.OrderId);
            notification.MarkSendFailed(ex.Message);
        }

        try
        {
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record notification {Type} for order {OrderId}", notification.Type, notification.OrderId);
        }
    }
}
