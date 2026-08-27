using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // How long after dispatch the provider should hold the delivery follow-up
    // before sending it. Must stay within the provider's scheduling window.
    private static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
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
        var body = $"eShop: thank you! Your order #{order.Id} has been placed. Total: ${order.Total():0.00}.";
        await SendAndRecordAsync(order, NotificationType.OrderPlaced, body, null, null, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"eShop: good news! Your order #{order.Id} is on its way.";
        await SendAndRecordAsync(order, NotificationType.OrderDispatched, dispatchedBody, null, null, cancellationToken);

        // The follow-up is queued with the provider itself (scheduled send),
        // not held in this application for a timer to send later.
        var followUpBody = $"eShop: your order #{order.Id} should have arrived by now. How did the delivery go?";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await SendAndRecordAsync(order, NotificationType.DeliveryFollowUp, followUpBody, sendAt, null, cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendAndRecordAsync(order, NotificationType.OrderCancelled, body, null, null, cancellationToken);

        // A cancelled order must never be followed by "how did the delivery go?":
        // call off any follow-up the provider is still holding.
        var spec = new OrderNotificationsByOrderSpecification(order.Id);
        var notifications = await _notificationRepository.ListAsync(spec, cancellationToken);
        foreach (var followUp in notifications.Where(n => n.Type == NotificationType.DeliveryFollowUp && !n.HasTerminalStatus))
        {
            await CancelFollowUpAsync(followUp, cancellationToken);
        }
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var keySpec = new OrderNotificationByIdempotencyKeySpecification(idempotencyKey);
        var existing = await _notificationRepository.FirstOrDefaultAsync(keySpec, cancellationToken);
        if (existing is not null)
        {
            return new ResendNotificationResult(ResendNotificationOutcome.AlreadyProcessed, existing);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendNotificationResult(ResendNotificationOutcome.NotFound, null);
        }

        if (original.IsContentRedacted || original.Body is null)
        {
            return new ResendNotificationResult(ResendNotificationOutcome.ContentRedacted, original);
        }

        // Bring the original's outcome up to date before acting on it.
        await RefreshStatusAsync(original, cancellationToken);

        var contactNumber = await GetCurrentContactNumberAsync(original.BuyerId, cancellationToken);
        if (contactNumber is null)
        {
            return new ResendNotificationResult(ResendNotificationOutcome.NoContactNumber, original);
        }

        var resend = await TrySendAsync(original.OrderId, original.BuyerId, original.Type, original.Body,
            contactNumber.PhoneNumber, null, idempotencyKey, cancellationToken);
        return new ResendNotificationResult(ResendNotificationOutcome.Sent, resend);
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.MessageSid is null || notification.HasTerminalStatus)
        {
            return;
        }

        try
        {
            var info = await _smsService.GetMessageAsync(notification.MessageSid, cancellationToken);
            if (info?.Status is not null)
            {
                notification.UpdateStatus(info.Status, info.ErrorCode, info.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not refresh status for notification {notification.Id}: {ex.Message}");
        }
    }

    private async Task CancelFollowUpAsync(OrderNotification followUp, CancellationToken cancellationToken)
    {
        if (followUp.MessageSid is null)
        {
            return;
        }

        try
        {
            var info = await _smsService.GetMessageAsync(followUp.MessageSid, cancellationToken);
            if (info is null)
            {
                return;
            }

            followUp.UpdateStatus(info.Status ?? followUp.Status, info.ErrorCode, info.ErrorMessage);

            if (string.Equals(info.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                var result = await _smsService.CancelScheduledAsync(followUp.MessageSid, cancellationToken);
                followUp.UpdateStatus(result.Status ?? "canceled", result.ErrorCode, result.ErrorMessage);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not cancel scheduled follow-up {followUp.Id}: {ex.Message}");
        }
    }

    private async Task SendAndRecordAsync(Order order, NotificationType type, string body,
        DateTimeOffset? scheduleAt, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var contactNumber = await GetCurrentContactNumberAsync(order.BuyerId, cancellationToken);
        if (contactNumber is null)
        {
            // A shopper with no number on file is simply not messaged.
            return;
        }

        await TrySendAsync(order.Id, order.BuyerId, type, body, contactNumber.PhoneNumber, scheduleAt, idempotencyKey, cancellationToken);
    }

    private async Task<OrderNotification> TrySendAsync(int orderId, string buyerId, NotificationType type,
        string body, string to, DateTimeOffset? scheduleAt, string? idempotencyKey, CancellationToken cancellationToken)
    {
        OrderNotification notification;
        try
        {
            var result = await _smsService.SendAsync(to, body, scheduleAt, cancellationToken);
            notification = new OrderNotification(orderId, buyerId, type,
                result.MessageSid, body, result.Status ?? (result.Success ? "accepted" : "failed"),
                result.ErrorCode, result.ErrorMessage, scheduleAt, idempotencyKey);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning($"SMS send for order {orderId} ({type}) failed: {ex.Message}");
            notification = new OrderNotification(orderId, buyerId, type,
                null, body, "failed", null, ex.Message, scheduleAt, idempotencyKey);
        }

        return await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task<ContactNumber?> GetCurrentContactNumberAsync(string buyerId, CancellationToken cancellationToken)
    {
        var spec = new ContactNumbersByBuyerSpecification(buyerId);
        var numbers = await _contactNumberRepository.ListAsync(spec, cancellationToken);
        return numbers.FirstOrDefault();
    }
}
