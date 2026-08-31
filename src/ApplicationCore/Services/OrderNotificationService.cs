using System;
using System.Collections.Generic;
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
    // How long after dispatch the delivery follow-up goes out.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

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
        var body = $"eShopOnWeb: your order #{order.Id} has been placed. Thank you for shopping with us!";
        await NotifySafelyAsync(order, OrderNotificationType.OrderPlaced, body, cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: good news — your order #{order.Id} has been dispatched and is on its way.";
        await NotifySafelyAsync(order, OrderNotificationType.OrderDispatched, body, cancellationToken);

        // Queue the delivery follow-up with the provider itself (scheduled send),
        // so nothing in this application has to wake up later to send it.
        var followUpBody = $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";
        await NotifySafelyAsync(order, OrderNotificationType.DeliveryFollowUp, followUpBody, cancellationToken,
            scheduleFor: DateTimeOffset.UtcNow.Add(FollowUpDelay));
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        // A follow-up that has not yet gone out must never reach the shopper.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShopOnWeb: your order #{order.Id} has been cancelled. Sorry for any inconvenience.";
        await NotifySafelyAsync(order, OrderNotificationType.OrderCancelled, body, cancellationToken);
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        var existing = await _notificationRepository
            .FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing != null)
        {
            // Same key seen before: do not send a second message.
            return existing;
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (original == null)
        {
            throw new InvalidOperationException($"Notification {notificationId} was not found.");
        }
        if (original.ContentRedacted || original.Body == null)
        {
            throw new InvalidOperationException("The content of this notification has been disposed of and can no longer be sent.");
        }

        // Never send to a number that is no longer registered to the shopper.
        var contactNumber = await ResolveContactNumberAsync(original.BuyerId, original.ContactNumberId, cancellationToken);
        if (contactNumber == null)
        {
            throw new InvalidOperationException("The shopper no longer has the destination number on file; the message cannot be re-sent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, contactNumber.Id,
            original.NotificationType, original.Body, idempotencyKey: idempotencyKey);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        var result = await _smsService.SendAsync(contactNumber.PhoneNumber, original.Body, cancellationToken);
        resend.MarkAccepted(result.MessageSid, result.Status);
        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        _logger.LogInformation("Re-sent notification {OriginalId} as notification {ResendId} (provider message accepted).",
            notificationId, resend.Id);
        return resend;
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            throw new InvalidOperationException($"Notification {notificationId} was not found.");
        }

        if (!notification.ContentRedacted && notification.MessageSid != null)
        {
            // Redact at the provider too, so the text is no longer retrievable there.
            await _smsService.RedactBodyAsync(notification.MessageSid, cancellationToken);
        }

        notification.MarkContentRedacted();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return notification;
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            if (notification.MessageSid == null ||
                OrderNotification.TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }

            try
            {
                var current = await _smsService.FetchAsync(notification.MessageSid, cancellationToken);
                if (current != null && current.Status != notification.Status)
                {
                    notification.UpdateStatus(current.Status, current.ErrorCode, current.ErrorMessage);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not refresh status for notification {NotificationId}: {Error}",
                    notification.Id, ex.Message);
            }
        }
    }

    public async Task<NotificationReconciliation> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerMessages = await _smsService.ListSentFromShopNumberAsync(from, to, cancellationToken);
        var localNotifications = await _notificationRepository
            .ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);

        var localBySid = localNotifications
            .Where(n => n.MessageSid != null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());
        var providerSids = providerMessages.Select(m => m.MessageSid).ToHashSet();

        var reconciliation = new NotificationReconciliation
        {
            From = from,
            To = to,
            ProviderMessages = providerMessages.ToList(),
            MatchedMessageSids = providerMessages.Where(m => localBySid.ContainsKey(m.MessageSid)).Select(m => m.MessageSid).ToList(),
            MissingLocally = providerMessages.Where(m => !localBySid.ContainsKey(m.MessageSid)).ToList(),
            MissingAtProvider = localNotifications.Where(n => n.MessageSid != null && !providerSids.Contains(n.MessageSid)).ToList()
        };
        return reconciliation;
    }

    private async Task NotifySafelyAsync(Order order, OrderNotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduleFor = null)
    {
        // A shopper with no number on file is simply not messaged.
        var contactNumber = await ResolveContactNumberAsync(order.BuyerId, null, cancellationToken);
        if (contactNumber == null)
        {
            _logger.LogInformation("Order {OrderId}: shopper has no contact number on file; no {Type} notification sent.",
                order.Id, type);
            return;
        }

        var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.Id, type, body, scheduleFor);
        await _notificationRepository.AddAsync(notification, cancellationToken);

        try
        {
            SmsSendResult result = scheduleFor.HasValue
                ? await _smsService.ScheduleAsync(contactNumber.PhoneNumber, body, scheduleFor.Value, cancellationToken)
                : await _smsService.SendAsync(contactNumber.PhoneNumber, body, cancellationToken);
            notification.MarkAccepted(result.MessageSid, result.Status);
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            notification.MarkFailed(ex is SmsProviderException providerEx ? providerEx.ProviderErrorCode?.ToString() : null,
                "The provider could not accept the message.");
            _logger.LogWarning("Order {OrderId}: {Type} notification {NotificationId} could not be sent: {Error}",
                order.Id, type, notification.Id, ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        var pendingFollowUps = notifications
            .Where(n => n.NotificationType == OrderNotificationType.DeliveryFollowUp
                        && n.MessageSid != null
                        && !OrderNotification.TerminalStatuses.Contains(n.Status))
            .ToList();

        foreach (var followUp in pendingFollowUps)
        {
            try
            {
                var cancelled = await _smsService.CancelScheduledAsync(followUp.MessageSid!, cancellationToken);
                followUp.UpdateStatus(cancelled?.Status ?? "canceled", cancelled?.ErrorCode, cancelled?.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {OrderId}: could not cancel scheduled follow-up notification {NotificationId} with the provider: {Error}",
                    orderId, followUp.Id, ex.Message);
            }
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }
    }

    /// <summary>
    /// The shopper's contact number to message: the requested one when still
    /// registered to that shopper, otherwise their first number on file.
    /// </summary>
    private async Task<ContactNumber?> ResolveContactNumberAsync(string buyerId, int? contactNumberId, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(buyerId), cancellationToken);
        if (contactNumberId.HasValue)
        {
            return numbers.FirstOrDefault(n => n.Id == contactNumberId.Value);
        }
        return numbers.FirstOrDefault();
    }
}
