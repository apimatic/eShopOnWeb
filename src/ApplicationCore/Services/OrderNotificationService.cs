using System;
using System.Collections.Generic;
using System.Globalization;
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
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessageProvider _messageProvider;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IMessageProvider messageProvider,
        IAppLogger<OrderNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messageProvider = messageProvider;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"Your eShop order #{order.Id} has been placed. " +
                   $"Total: {order.Total().ToString("C", CultureInfo.GetCultureInfo("en-US"))}. Thank you for shopping with us!";
        await SendToShopperAsync(order, NotificationType.OrderPlaced, body, ct);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"Good news! Your eShop order #{order.Id} is on its way.";
        await SendToShopperAsync(order, NotificationType.OrderDispatched, body, ct);

        var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await SendToShopperAsync(order, NotificationType.DeliveryFollowUp, followUpBody, ct, scheduleFor: sendAt);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, ct);

        var body = $"Your eShop order #{order.Id} has been cancelled. Please contact support if this is unexpected.";
        await SendToShopperAsync(order, NotificationType.OrderCancelled, body, ct);
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendResult { Notification = existing, AlreadyExisted = true };
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return new ResendResult { Failure = ResendFailure.NotFound };
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return new ResendResult { Failure = ResendFailure.ContentDisposed };
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            original.Type, original.Body, idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        try
        {
            var sent = await _messageProvider.SendMessageAsync(original.ToNumber, original.Body, ct);
            resend.MarkHandedToProvider(sent.Sid!, sent.Status);
            resend.UpdateProviderOutcome(sent.Status, sent.ErrorCode, sent.ErrorMessage);
        }
        catch (Exception ex) when (ex is MessageProviderException)
        {
            resend.MarkSendFailed();
            await _notifications.AddAsync(resend, ct);
            throw;
        }

        await _notifications.AddAsync(resend, ct);
        return new ResendResult { Notification = resend };
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return false;
        }

        if (notification.ContentRedacted)
        {
            return true;
        }

        // The provider copy goes first: only once the text is gone there do we drop ours.
        if (notification.ProviderMessageSid is not null)
        {
            await _messageProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, ct);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, ct);
        return true;
    }

    public async Task RefreshOutcomeAsync(OrderNotification notification, CancellationToken ct = default)
    {
        if (notification.SendFailed || notification.ProviderMessageSid is null)
        {
            return;
        }

        try
        {
            var current = await _messageProvider.GetMessageAsync(notification.ProviderMessageSid, ct);
            notification.UpdateProviderOutcome(current.Status, current.ErrorCode, current.ErrorMessage);
            await _notifications.UpdateAsync(notification, ct);
        }
        catch (MessageProviderException ex)
        {
            _logger.LogWarning("Could not refresh outcome for notification {NotificationId}: {Reason}",
                notification.Id, ex.Message);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpecification(orderId), ct);
        foreach (var followUp in pending)
        {
            try
            {
                var cancelled = await _messageProvider.CancelScheduledMessageAsync(followUp.ProviderMessageSid!, ct);
                followUp.UpdateProviderOutcome(cancelled.Status, cancelled.ErrorCode, cancelled.ErrorMessage);
            }
            catch (MessageProviderException ex)
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {NotificationId} at provider: {Reason}",
                    followUp.Id, ex.Message);
                followUp.UpdateProviderOutcome("cancel-failed", null, null);
            }

            await _notifications.UpdateAsync(followUp, ct);
        }
    }

    private async Task SendToShopperAsync(Order order, NotificationType type, string body,
        CancellationToken ct, DateTimeOffset? scheduleFor = null)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
        if (numbers.Count == 0)
        {
            return;
        }

        foreach (var number in numbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, number.PhoneNumber,
                type, body, scheduledFor: scheduleFor);

            try
            {
                var sent = scheduleFor.HasValue
                    ? await _messageProvider.ScheduleMessageAsync(number.PhoneNumber, body, scheduleFor.Value, ct)
                    : await _messageProvider.SendMessageAsync(number.PhoneNumber, body, ct);

                notification.MarkHandedToProvider(sent.Sid!, sent.Status);
                notification.UpdateProviderOutcome(sent.Status, sent.ErrorCode, sent.ErrorMessage);
            }
            catch (Exception ex) when (ex is MessageProviderException)
            {
                // A message that cannot be sent must never fail the order operation.
                notification.MarkSendFailed();
                _logger.LogWarning("Notification of type {Type} for order {OrderId} could not be sent: {Reason}",
                    type, order.Id, ex.Message);
            }

            await _notifications.AddAsync(notification, ct);
        }
    }
}
