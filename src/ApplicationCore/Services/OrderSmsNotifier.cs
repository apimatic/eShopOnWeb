using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderSmsNotifier
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderSmsNotifier> _logger;

    public OrderSmsNotifier(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderSmsNotifier> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<ContactNumber?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return contacts.FirstOrDefault();
    }

    public async Task<bool> IsDestinationActiveAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return contacts.Any(c => c.CanonicalNumber == canonicalNumber);
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken) =>
        TrySendAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed.",
            scheduleFor: null,
            cancellationToken);

    public Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken) =>
        TrySendAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            scheduleFor: null,
            cancellationToken);

    public Task QueueDeliveryFollowUpAsync(Order order, CancellationToken cancellationToken) =>
        TrySendAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            scheduleFor: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

    public Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken) =>
        TrySendAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            scheduleFor: null,
            cancellationToken);

    public async Task CancelQueuedFollowUpsAsync(Order order, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(
            new ScheduledFollowUpNotificationsSpecification(order.Id),
            cancellationToken);

        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(followUp.ProviderMessageSid, cancellationToken);
                if (current != null && !string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
                {
                    followUp.ApplyProviderState(current.Sid, current.Status, current.ErrorCode, current.DateSent);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    continue;
                }

                var cancelled = await _messaging.CancelScheduledAsync(followUp.ProviderMessageSid, cancellationToken);
                followUp.ApplyProviderState(cancelled.Sid, cancelled.Status, cancelled.ErrorCode, cancelled.DateSent);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to cancel a queued follow-up for order {OrderId}.", order.Id);
            }
        }
    }

    public async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (current == null)
                {
                    continue;
                }

                notification.ApplyProviderState(current.Sid, current.Status, current.ErrorCode, current.DateSent);
                if (string.IsNullOrEmpty(current.Body) && !string.IsNullOrEmpty(notification.Body))
                {
                    notification.RedactContent();
                }

                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task<OrderNotification?> TryResendAsync(
        OrderNotification original,
        string destinationNumber,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = original.Body;
        if (original.ContentRedacted || string.IsNullOrEmpty(body))
        {
            return null;
        }

        TwilioMessageRecord? sent = null;
        try
        {
            sent = await _messaging.SendAsync(destinationNumber, body, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to resend notification {NotificationId}.", original.Id);
        }

        var resend = new OrderNotification(
            original.OrderId,
            original.BuyerId,
            NotificationKind.Resend,
            destinationNumber,
            body,
            sent?.Sid,
            sent?.Status ?? "failed",
            sourceNotificationId: original.Id,
            idempotencyKey: idempotencyKey);

        if (sent != null)
        {
            resend.ApplyProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.DateSent);
        }

        await _notifications.AddAsync(resend, cancellationToken);
        return resend;
    }

    private async Task TrySendAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduleFor,
        CancellationToken cancellationToken)
    {
        ContactNumber? destination;
        try
        {
            destination = await GetActiveDestinationAsync(order.BuyerId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to load a contact number while notifying order {OrderId}.", order.Id);
            return;
        }

        if (destination == null)
        {
            return;
        }

        TwilioMessageRecord? sent = null;
        try
        {
            sent = scheduleFor.HasValue
                ? await _messaging.ScheduleAsync(destination.CanonicalNumber, body, scheduleFor.Value, cancellationToken)
                : await _messaging.SendAsync(destination.CanonicalNumber, body, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to send {Kind} notification for order {OrderId}.", kind, order.Id);
        }

        try
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                destination.CanonicalNumber,
                body,
                sent?.Sid,
                sent?.Status ?? "failed",
                scheduleFor);

            if (sent != null)
            {
                notification.ApplyProviderState(sent.Sid, sent.Status, sent.ErrorCode, sent.DateSent);
            }

            await _notifications.AddAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to persist {Kind} notification for order {OrderId}.", kind, order.Id);
        }
    }
}
