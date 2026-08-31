using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates shopper notifications as orders move. Sending is best-effort for order
/// operations (a message that cannot be sent never fails the underlying operation); operator
/// actions (resend, redact, reconcile) surface provider failures to the operator.
/// Destination numbers are never written to logs.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITextMessagingService _messagingService;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly TimeSpan _followUpDelay;

    public OrderNotificationService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ITextMessagingService messagingService,
        IAppLogger<OrderNotificationService> logger,
        TimeSpan followUpDelay)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingService = messagingService;
        _logger = logger;
        _followUpDelay = followUpDelay;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await NotifySafelyAsync(order, NotificationType.OrderPlaced,
            $"eShop: your order #{order.Id} has been placed. Thank you for shopping with us!",
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            var contactNumbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken);
            if (contactNumbers.Count == 0)
            {
                return;
            }

            var body = $"eShop: good news - your order #{order.Id} has been dispatched and is on its way.";
            var followUpBody = $"eShop: how did the delivery of your order #{order.Id} go? We would love to hear from you.";
            var followUpAt = DateTimeOffset.UtcNow.Add(_followUpDelay);

            foreach (var contactNumber in contactNumbers)
            {
                var sent = await SendAndRecordAsync(order, contactNumber, NotificationType.OrderDispatched, body, cancellationToken);
                if (sent)
                {
                    await ScheduleFollowUpAsync(order, contactNumber, followUpBody, followUpAt, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to notify buyer of dispatch for order {OrderId}. The order operation still succeeds. Reason: {Reason}", order.Id, DescribeSafely(ex));
        }
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            await CancelScheduledFollowUpsForOrderAsync(order.Id, cancellationToken);

            await NotifySafelyAsync(order, NotificationType.OrderCancelled,
                $"eShop: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to notify buyer of cancellation for order {OrderId}. The order operation still succeeds. Reason: {Reason}", order.Id, DescribeSafely(ex));
        }
    }

    public async Task CancelScheduledForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default)
    {
        try
        {
            var scheduled = await _notificationRepository.ListAsync(
                new ScheduledNotificationsByContactNumberSpecification(contactNumberId), cancellationToken);
            foreach (var notification in scheduled)
            {
                await CancelScheduledNotificationAsync(notification, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to cancel scheduled messages for contact number {ContactNumberId}. The delete operation still succeeds. Reason: {Reason}", contactNumberId, DescribeSafely(ex));
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.ListAsync(
            new OrderNotificationsByOrderSpecification(orderId), cancellationToken);

        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageId))
            {
                continue;
            }

            try
            {
                var outcome = await _messagingService.GetDeliveryOutcomeAsync(notification.ProviderMessageId, cancellationToken);
                notification.UpdateDeliveryOutcome(outcome.Status, outcome.ErrorCode, outcome.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                // Refresh is best effort: keep the last known outcome rather than failing the read.
                _logger.LogWarning("Could not refresh delivery outcome for notification {NotificationId}; returning last known state. Reason: {Reason}", notification.Id, DescribeSafely(ex));
            }
        }

        return notifications;
    }

    public async Task<ResendResult> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            return new ResendResult(existing, alreadyExisted: true);
        }

        if (original.ContentRedacted || original.Body is null)
        {
            throw new NotificationConflictException("The message content has been disposed of and can no longer be sent.");
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contactNumber is null)
        {
            throw new NotificationConflictException("The destination number is no longer registered; nothing may be sent to it again.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            contactNumber.PhoneNumber, original.NotificationType, original.Body,
            resendOfId: original.Id, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var sent = await _messagingService.SendAsync(contactNumber.PhoneNumber, original.Body, cancellationToken);
            resend.MarkSent(sent.ProviderMessageId, sent.Status);
        }
        catch (Exception ex)
        {
            resend.MarkSendFailed("failed", null, "The provider did not accept the message.");
            await _notificationRepository.UpdateAsync(resend, cancellationToken);
            throw RethrowAsProviderFailure(ex);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);
        return new ResendResult(resend, alreadyExisted: false);
    }

    public async Task RedactContentAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.ContentRedacted)
        {
            return;
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageId))
        {
            await _messagingService.RedactBodyAsync(notification.ProviderMessageId, cancellationToken);
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliation> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerResult = await _messagingService.ListSentMessagesAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var localByProviderId = localNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageId))
            .GroupBy(n => n.ProviderMessageId!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new NotificationReconciliation
        {
            From = from,
            To = to,
            Truncated = providerResult.Truncated
        };

        var matchedLocalIds = new HashSet<int>();

        foreach (var providerMessage in providerResult.Messages)
        {
            if (providerMessage.ProviderMessageId != null
                && localByProviderId.TryGetValue(providerMessage.ProviderMessageId, out var local))
            {
                matchedLocalIds.Add(local.Id);
                report.Matched.Add(new ReconciliationEntry
                {
                    ProviderMessageId = providerMessage.ProviderMessageId,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    ProviderStatus = providerMessage.Status,
                    LocalStatus = local.ProviderStatus,
                    DateSent = providerMessage.DateSent,
                    ProviderBody = providerMessage.Body
                });
            }
            else
            {
                report.ProviderOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageId = providerMessage.ProviderMessageId,
                    ProviderStatus = providerMessage.Status,
                    DateSent = providerMessage.DateSent,
                    ProviderBody = providerMessage.Body
                });
            }
        }

        foreach (var local in localNotifications)
        {
            if (!matchedLocalIds.Contains(local.Id))
            {
                report.LocalOnly.Add(new ReconciliationEntry
                {
                    ProviderMessageId = local.ProviderMessageId,
                    NotificationId = local.Id,
                    OrderId = local.OrderId,
                    LocalStatus = local.ProviderStatus
                });
            }
        }

        return report;
    }

    private async Task NotifySafelyAsync(Order order, NotificationType type, string body, CancellationToken cancellationToken)
    {
        try
        {
            var contactNumbers = await GetContactNumbersAsync(order.BuyerId, cancellationToken);
            foreach (var contactNumber in contactNumbers)
            {
                await SendAndRecordAsync(order, contactNumber, type, body, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send {NotificationType} notification for order {OrderId}. The order operation still succeeds. Reason: {Reason}", type, order.Id, DescribeSafely(ex));
        }
    }

    private async Task<List<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (contactNumbers.Count == 0)
        {
            _logger.LogInformation("Buyer has no contact numbers on file; no notification sent for the order event.");
        }
        return contactNumbers;
    }

    private async Task<bool> SendAndRecordAsync(Order order, ContactNumber contactNumber, NotificationType type, string body, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber, type, body);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var sent = await _messagingService.SendAsync(contactNumber.PhoneNumber, body, cancellationToken);
            notification.MarkSent(sent.ProviderMessageId, sent.Status);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("failed", null, "The provider did not accept the message.");
            _logger.LogWarning("Message for notification {NotificationId} (order {OrderId}) was not accepted by the provider. Reason: {Reason}", notification.Id, order.Id, DescribeSafely(ex));
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        return notification.ProviderMessageId != null;
    }

    private async Task ScheduleFollowUpAsync(Order order, ContactNumber contactNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, contactNumber.PhoneNumber,
            NotificationType.DeliveryFollowUp, body, scheduledFor: sendAt);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            var scheduled = await _messagingService.ScheduleAsync(contactNumber.PhoneNumber, body, sendAt, cancellationToken);
            notification.MarkSent(scheduled.ProviderMessageId, scheduled.Status);
        }
        catch (Exception ex)
        {
            notification.MarkSendFailed("failed", null, "The provider did not accept the scheduled message.");
            _logger.LogWarning("Follow-up for notification {NotificationId} (order {OrderId}) could not be scheduled with the provider. Reason: {Reason}", notification.Id, order.Id, DescribeSafely(ex));
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelScheduledFollowUpsForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in scheduled)
        {
            await CancelScheduledNotificationAsync(notification, cancellationToken);
        }
    }

    private async Task CancelScheduledNotificationAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _messagingService.CancelScheduledAsync(notification.ProviderMessageId!, cancellationToken);
            notification.UpdateDeliveryOutcome("canceled", null, null);
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not cancel scheduled provider message for notification {NotificationId}. Reason: {Reason}", notification.Id, DescribeSafely(ex));
        }
    }

    /// <summary>
    /// Log-safe description of a failure: our own exception messages are caller-safe by
    /// construction; anything else is reduced to its type name so provider bodies (which can
    /// embed destination numbers) never reach the logs.
    /// </summary>
    private static string DescribeSafely(Exception ex) =>
        ex is MessagingProviderException providerException ? providerException.Message : ex.GetType().Name;

    private static Exception RethrowAsProviderFailure(Exception ex) => ex switch
    {
        MessagingProviderException => ex,
        _ => new MessagingProviderException("The messaging provider could not complete the request.", null, ex)
    };
}
