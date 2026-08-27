using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // Twilio requires scheduled messages to be between 15 minutes and 35 days out.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private static readonly HashSet<string> ResendableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        OrderNotification.SendFailedStatus, "failed", "undelivered"
    };

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

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToBuyerAsync(order, NotificationType.OrderPlaced,
            $"eShop: Your order #{order.Id} has been placed. Thank you for shopping with us!",
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await SendToBuyerAsync(order, NotificationType.OrderDispatched,
            $"eShop: Good news! Your order #{order.Id} is on its way.",
            cancellationToken);

        var followUpBody = $"eShop: Your order #{order.Id} should have arrived by now. How did the delivery go?";
        await ScheduleForBuyerAsync(order, followUpBody, DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A follow-up that has not yet gone out must never reach the shopper.
        var pendingSpec = new PendingOrderNotificationsSpecification(order.Id);
        var pending = await _notificationRepository.ListAsync(pendingSpec, cancellationToken);
        foreach (var notification in pending)
        {
            await CancelScheduledNotificationAsync(notification, cancellationToken);
        }

        await SendToBuyerAsync(order, NotificationType.OrderCancelled,
            $"eShop: Your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
            cancellationToken);
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var keySpec = new NotificationByIdempotencyKeySpecification(idempotencyKey);
        var existing = await _notificationRepository.FirstOrDefaultAsync(keySpec, cancellationToken);
        if (existing != null)
        {
            return new ResendNotificationResult { Status = ResendNotificationStatus.AlreadyProcessed, Notification = existing };
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            return new ResendNotificationResult { Status = ResendNotificationStatus.NotFound };
        }

        await RefreshStatusAsync(original, cancellationToken);

        if (!ResendableStatuses.Contains(original.Status))
        {
            return new ResendNotificationResult { Status = ResendNotificationStatus.NotResendable, Notification = original };
        }

        if (original.ContentRedacted || string.IsNullOrEmpty(original.Body))
        {
            return new ResendNotificationResult { Status = ResendNotificationStatus.ContentRedacted, Notification = original };
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationType.Resend, original.ToNumber, original.Body);
        resend.AssignIdempotencyKey(idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        var result = await SendAndRecordAsync(resend, cancellationToken);
        return new ResendNotificationResult { Status = ResendNotificationStatus.Resent, Notification = resend };
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        if (!notification.ContentRedacted && !string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            try
            {
                await _smsProvider.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to redact message content at the provider for notification {NotificationId}: {Message}", notification.Id, ex.Message);
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task SuppressPendingMessagesToAsync(ContactNumber contactNumber, CancellationToken cancellationToken = default)
    {
        var spec = new PendingNotificationsForNumberSpecification(contactNumber.PhoneNumber);
        var pending = await _notificationRepository.ListAsync(spec, cancellationToken);
        foreach (var notification in pending)
        {
            await CancelScheduledNotificationAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        // Terminal states will not change at the provider; skip the call.
        if (notification.Status is "delivered" or "canceled" or "undelivered" or "failed" or OrderNotification.SendFailedStatus)
        {
            return;
        }

        try
        {
            var info = await _smsProvider.GetMessageAsync(notification.ProviderMessageSid, cancellationToken);
            if (info != null && !string.Equals(info.Status, notification.Status, StringComparison.OrdinalIgnoreCase))
            {
                notification.UpdateStatus(info.Status, info.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not refresh status for notification {NotificationId}: {Message}", notification.Id, ex.Message);
        }
    }

    private async Task SendToBuyerAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, type, number.PhoneNumber, body);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            await SendAndRecordAsync(notification, cancellationToken);
        }
    }

    private async Task ScheduleForBuyerAsync(Order order, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var numbers = await GetBuyerNumbersAsync(order.BuyerId, cancellationToken);
        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, NotificationType.DeliveryFollowUp, number.PhoneNumber, body);
            await _notificationRepository.AddAsync(notification, cancellationToken);
            try
            {
                var result = await _smsProvider.ScheduleAsync(number.PhoneNumber, body, sendAt, cancellationToken);
                if (result.Success)
                {
                    notification.MarkAccepted(result.ProviderMessageSid!, result.ProviderStatus ?? "scheduled", sendAt);
                }
                else
                {
                    notification.MarkSendFailed(result.ErrorMessage ?? "Provider rejected the scheduled message.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to schedule follow-up for order {OrderId}: {Message}", order.Id, ex.Message);
                notification.MarkSendFailed("Scheduling failed: " + ex.Message);
            }
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    private async Task<SmsSendResult> SendAndRecordAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        SmsSendResult result;
        try
        {
            result = await _smsProvider.SendAsync(notification.ToNumber, notification.Body!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send notification {NotificationId} for order {OrderId}: {Message}", notification.Id, notification.OrderId, ex.Message);
            result = SmsSendResult.Failed("Send failed: " + ex.Message);
        }

        if (result.Success)
        {
            notification.MarkAccepted(result.ProviderMessageSid!, result.ProviderStatus ?? "queued");
        }
        else
        {
            notification.MarkSendFailed(result.ErrorMessage ?? "Unknown provider error.");
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return result;
    }

    private async Task CancelScheduledNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (!notification.IsScheduled || string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var cancelled = await _smsProvider.CancelScheduledAsync(notification.ProviderMessageSid, cancellationToken);
            notification.UpdateStatus(cancelled ? "canceled" : notification.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to cancel scheduled message for notification {NotificationId}: {Message}", notification.Id, ex.Message);
        }
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task<List<ContactNumber>> GetBuyerNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var spec = new ContactNumbersByBuyerSpecification(buyerId);
        return await _contactNumberRepository.ListAsync(spec, cancellationToken);
    }
}
