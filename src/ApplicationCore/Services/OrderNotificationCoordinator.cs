using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationCoordinator
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderNotificationCoordinator> _logger;

    public OrderNotificationCoordinator(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderNotificationCoordinator> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken)
    {
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            cancellationToken);
    }

    public async Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken)
    {
        await TrySendAsync(
            order,
            OrderNotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShop order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await TrySendAsync(
            order,
            OrderNotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot?.Status is { Length: > 0 })
                {
                    notification.ApplyProviderOutcome(snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to refresh provider status for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    public async Task<OrderNotification> SendResendAsync(
        OrderNotification source,
        string destinationNumber,
        int? contactNumberId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = source.Body;
        if (string.IsNullOrEmpty(body))
        {
            throw new InvalidOperationException("The original message content is no longer available to resend.");
        }

        var resend = new OrderNotification(
            source.OrderId,
            source.BuyerId,
            OrderNotificationKind.Resend,
            destinationNumber,
            body,
            contactNumberId,
            idempotencyKey,
            source.Id);

        resend = await _notifications.AddAsync(resend, cancellationToken);
        await DeliverAsync(resend, sendAt: null, cancellationToken);
        return resend;
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpNotificationsSpec(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                continue;
            }

            try
            {
                var snapshot = await _messagingClient.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderOutcome(
                    snapshot.Status ?? "canceled",
                    snapshot.ErrorCode,
                    snapshot.ErrorMessage);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning(
                    "Failed to cancel a pending follow-up for notification {NotificationId}.",
                    notification.Id);
            }
        }
    }

    private async Task TrySendAsync(
        Order order,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        ContactNumber? contact;
        try
        {
            var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerIdSpec(order.BuyerId), cancellationToken);
            contact = contacts.FirstOrDefault();
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to load a contact number while sending an order notification.");
            return;
        }

        if (contact is null)
        {
            return;
        }

        try
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                kind,
                contact.PhoneNumber,
                body,
                contact.Id,
                scheduledSendAt: sendAt);

            notification = await _notifications.AddAsync(notification, cancellationToken);
            await DeliverAsync(notification, sendAt, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to record or send an order notification of kind {Kind}.", kind);
        }
    }

    private async Task DeliverAsync(OrderNotification notification, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _messagingClient.SendAsync(
                new SmsSendRequest
                {
                    To = notification.DestinationNumber,
                    Body = notification.Body ?? string.Empty,
                    SendAt = sendAt
                },
                cancellationToken);

            if (!string.IsNullOrEmpty(snapshot.Sid) && !string.IsNullOrEmpty(snapshot.Status))
            {
                notification.RecordProviderAccepted(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            }
            else
            {
                notification.RecordSendFailure(snapshot.Status ?? "failed", snapshot.ErrorCode, snapshot.ErrorMessage);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            notification.RecordSendFailure("failed", null, "The messaging provider rejected or could not accept the message.");
            await _notifications.UpdateAsync(notification, cancellationToken);
            _logger.LogWarning("Messaging provider call failed for notification {NotificationId}.", notification.Id);
        }
    }
}
