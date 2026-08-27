using System;
using System.Collections.Generic;
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
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly NotificationSettings _settings;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway,
        NotificationSettings settings,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _settings = settings;
        _logger = logger;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!";
        return NotifyAllContactNumbersAsync(order, NotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: good news! Your order #{order.Id} is on its way.";
        await NotifyAllContactNumbersAsync(order, NotificationType.OrderDispatched, body, cancellationToken);

        // Queue the delivery follow-up with the provider itself (scheduled send),
        // so nothing in this application has to wake up later to send it.
        var sendAt = DateTimeOffset.UtcNow.AddDays(_settings.FollowUpDelayDays);
        var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We would love your feedback.";
        await NotifyAllContactNumbersAsync(order, NotificationType.DeliveryFollowUp, followUpBody, cancellationToken, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShop: your order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await NotifyAllContactNumbersAsync(order, NotificationType.OrderCancelled, body, cancellationToken);

        // A follow-up that has not yet gone out must never reach the shopper of a cancelled order.
        var pendingFollowUps = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(order.Id), cancellationToken);
        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var cancelled = await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateFromProvider(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
            }
            catch (Exception ex)
            {
                // Never log ex.Message here: provider error messages can contain the destination number.
                _logger.LogWarning($"Failed to cancel scheduled follow-up notification {followUp.Id} (provider message {followUp.ProviderMessageSid}): {DescribeSafely(ex)}");
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            // Replay under the same key: no second message is sent.
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            throw new KeyNotFoundException($"Notification {notificationId} was not found.");
        }
        if (original.ContentRedacted)
        {
            throw new NotificationContentRedactedException($"Notification {notificationId} content has been disposed of and cannot be re-sent.");
        }

        // A removed contact number must never be messaged again.
        var contactNumber = original.ContactNumberId.HasValue
            ? await _contactNumberRepository.GetByIdAsync(original.ContactNumberId.Value, cancellationToken)
            : null;
        if (contactNumber is null || contactNumber.BuyerId != original.BuyerId)
        {
            throw new ContactNumberRemovedException($"The contact number for notification {notificationId} is no longer registered; it must not be messaged again.");
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            contactNumber.Id,
            contactNumber.PhoneNumber,
            NotificationType.Resend,
            original.Body,
            idempotencyKey: idempotencyKey);

        await SendAndRecordAsync(resend, contactNumber.PhoneNumber, scheduledForUtc: null, cancellationToken);
        return resend;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            throw new KeyNotFoundException($"Notification {notificationId} was not found.");
        }

        if (!notification.ContentRedacted)
        {
            if (notification.ProviderMessageSid is not null)
            {
                // Redact at the provider too, not merely in this application.
                await _smsGateway.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            notification.RedactContent();
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ProviderMessageSid is null)
        {
            return;
        }

        try
        {
            var providerMessage = await _smsGateway.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (providerMessage is not null)
            {
                notification.UpdateFromProvider(providerMessage.Status, providerMessage.ErrorCode, providerMessage.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Status refresh is best-effort; never fail the caller's request over it.
            _logger.LogWarning($"Failed to refresh status for notification {notification.Id} (provider message {notification.ProviderMessageSid}): {DescribeSafely(ex)}");
        }
    }

    private static string DescribeSafely(Exception ex)
    {
        // Provider exceptions carry a machine-readable code; use it instead of the
        // raw message, which may embed the shopper's phone number.
        var code = ex is ProviderException providerEx && providerEx.ErrorCode.HasValue
            ? $" provider error {providerEx.ErrorCode}."
            : string.Empty;
        return $"{ex.GetType().Name}.{code}";
    }

    private async Task NotifyAllContactNumbersAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken, DateTimeOffset? scheduledForUtc = null)
    {
        List<ContactNumber> contactNumbers;
        try
        {
            contactNumbers = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load contact numbers for order {order.Id}; shopper will not be messaged. {ex.Message}");
            return;
        }

        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber, type, body, scheduledForUtc);
            await SendAndRecordAsync(notification, contactNumber.PhoneNumber, scheduledForUtc, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(OrderNotification notification, string toNumber, DateTimeOffset? scheduledForUtc, CancellationToken cancellationToken)
    {
        try
        {
            ProviderMessage sent = scheduledForUtc.HasValue
                ? await _smsGateway.ScheduleAsync(toNumber, notification.Body, scheduledForUtc.Value, cancellationToken)
                : await _smsGateway.SendAsync(toNumber, notification.Body, cancellationToken);
            notification.MarkProviderAccepted(sent.Sid, sent.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            // Never log ex.Message here: provider error messages can contain the destination number.
            _logger.LogWarning($"SMS for notification (order {notification.OrderId}, type {notification.Type}) was not accepted by the provider: {DescribeSafely(ex)}");
            notification.MarkSendFailed(DescribeSafely(ex));
        }

        try
        {
            await _notificationRepository.AddAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to record notification for order {notification.OrderId}: {ex.Message}");
        }
    }
}
