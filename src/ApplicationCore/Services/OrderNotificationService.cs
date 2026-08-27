using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
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
        var body = $"eShopOnWeb: thank you! Your order #{order.Id} has been placed.";
        await SendToShopperAsync(order, OrderNotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news! Your order #{order.Id} is on its way.";
        await SendToShopperAsync(order, OrderNotificationType.OrderDispatched, body, cancellationToken);

        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        await SendToShopperAsync(order, OrderNotificationType.DeliveryFollowUp, followUpBody, cancellationToken, sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A queued follow-up must never reach a shopper whose order was cancelled.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Contact support if this is unexpected.";
        await SendToShopperAsync(order, OrderNotificationType.OrderCancelled, body, cancellationToken);
    }

    public async Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existingForKey = await _notificationRepository
            .FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existingForKey is not null)
        {
            _logger.LogInformation("Resend under idempotency key already produced notification {NotificationId}; not sending again.", existingForKey.Id);
            return new ResendNotificationResult(ResendNotificationStatus.Duplicate, existingForKey);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original is null)
        {
            return new ResendNotificationResult(ResendNotificationStatus.NotFound, null);
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return new ResendNotificationResult(ResendNotificationStatus.ContentUnavailable, null);
        }

        // A removed contact number must never be messaged again.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(original.BuyerId, original.ToNumber), cancellationToken);
        if (contactNumber is null)
        {
            _logger.LogWarning("Resend of notification {NotificationId} refused: destination is no longer registered.", notificationId);
            return new ResendNotificationResult(ResendNotificationStatus.DestinationUnavailable, null);
        }

        SmsSendResult sendResult;
        try
        {
            sendResult = await _smsService.SendAsync(original.ToNumber, original.Body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Resend of notification {NotificationId} failed at provider: {Error}", notificationId, ex.Message ?? string.Empty);
            return new ResendNotificationResult(ResendNotificationStatus.ProviderError, null);
        }

        var notification = new OrderNotification(
            original.OrderId, original.BuyerId, original.ToNumber, original.Type,
            original.Body, sendResult.MessageSid,
            sendResult.Success ? (sendResult.Status ?? OrderNotificationStatuses.Queued) : OrderNotificationStatuses.Rejected,
            errorCode: sendResult.ErrorCode,
            idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        notification = await _notificationRepository.AddAsync(notification, cancellationToken);
        _logger.LogInformation("Resent notification {OriginalNotificationId} as notification {NotificationId} (provider status {Status}).",
            original.Id, notification.Id, notification.Status);

        return new ResendNotificationResult(ResendNotificationStatus.Sent, notification);
    }

    public async Task<DeleteNotificationContentStatus> DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return DeleteNotificationContentStatus.NotFound;
        }

        if (!notification.ContentRedacted && !string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            bool providerRedacted;
            try
            {
                providerRedacted = await _smsService.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider content disposal failed for notification {NotificationId}: {Error}", notificationId, ex.Message);
                return DeleteNotificationContentStatus.ProviderError;
            }

            if (!providerRedacted)
            {
                return DeleteNotificationContentStatus.ProviderError;
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Content disposed for notification {NotificationId}.", notificationId);
        return DeleteNotificationContentStatus.Success;
    }

    private async Task SendToShopperAsync(Order order, OrderNotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduleFor = null)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);

        if (contactNumbers.Count == 0)
        {
            _logger.LogInformation("Order {OrderId}: buyer has no contact number on file; skipping {Type} notification.", order.Id, type);
            return;
        }

        foreach (var contactNumber in contactNumbers)
        {
            await SendAndRecordAsync(order, contactNumber.PhoneNumber, type, body, scheduleFor, cancellationToken);
        }
    }

    private async Task SendAndRecordAsync(Order order, string toNumber, OrderNotificationType type, string body,
        DateTimeOffset? scheduleFor, CancellationToken cancellationToken)
    {
        SmsSendResult sendResult;
        try
        {
            sendResult = scheduleFor.HasValue
                ? await _smsService.ScheduleAsync(toNumber, body, scheduleFor.Value, cancellationToken)
                : await _smsService.SendAsync(toNumber, body, cancellationToken);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Order {OrderId}: {Type} notification errored: {Error}", order.Id, type, ex.Message ?? string.Empty);
            sendResult = new SmsSendResult(false, null, null, null, ex.GetType().Name);
        }

        if (!sendResult.Success)
        {
            _logger.LogWarning("Order {OrderId}: {Type} notification not accepted by provider (error {ErrorCode}).", order.Id, type, sendResult.ErrorCode ?? "unknown");
        }

        var notification = new OrderNotification(
            order.Id, order.BuyerId, toNumber, type,
            body, sendResult.MessageSid,
            sendResult.Success ? (sendResult.Status ?? OrderNotificationStatuses.Queued) : OrderNotificationStatuses.Rejected,
            scheduledFor: scheduleFor,
            errorCode: sendResult.ErrorCode);

        await _notificationRepository.AddAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);

        foreach (var followUp in followUps)
        {
            try
            {
                var cancelled = await _smsService.CancelScheduledAsync(followUp.ProviderMessageSid!, cancellationToken);
                followUp.UpdateStatus(cancelled ? OrderNotificationStatuses.Canceled : followUp.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: failed to cancel scheduled follow-up (notification {NotificationId}): {Error}",
                    orderId, followUp.Id, ex.Message ?? string.Empty);
            }

            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }
}
