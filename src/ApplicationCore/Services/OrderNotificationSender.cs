using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationSender
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messaging;
    private readonly IAppLogger<OrderNotificationSender> _logger;

    public OrderNotificationSender(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messaging,
        IAppLogger<OrderNotificationSender> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task NotifySafelyAsync(
        Order order,
        OrderNotificationKind kind,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await NotifyAsync(order, kind, sendAt, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Order {OrderId} notification of kind {Kind} could not be sent; the order operation still succeeded.", order.Id, kind);
        }
    }

    public async Task CancelPendingFollowUpsSafelyAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(order.Id), cancellationToken);
            foreach (var notification in notifications)
            {
                if (!notification.IsCancellableFollowUp)
                {
                    continue;
                }

                try
                {
                    var updated = await _messaging.UpdateAsync(notification.ProviderMessageSid!, body: null, status: "canceled", cancellationToken);
                    notification.ApplyProviderState(updated.Status ?? "canceled", updated.ErrorCode, updated.ErrorMessage, updated.Body);
                    await _notifications.UpdateAsync(notification, cancellationToken);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Could not cancel follow-up notification {NotificationId} for order {OrderId}.", notification.Id, order.Id);
                }
            }
        }
        catch (Exception)
        {
            _logger.LogWarning("Follow-up cancellation for order {OrderId} did not complete; the cancel operation still succeeded.", order.Id);
        }
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
                var current = await _messaging.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                notification.ApplyProviderState(
                    current.Status ?? notification.ProviderStatus,
                    current.ErrorCode,
                    current.ErrorMessage,
                    current.Body);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task<OrderNotification> SendToDestinationAsync(
        Order order,
        OrderNotificationKind kind,
        string destinationNumber,
        int? contactNumberId,
        string body,
        int? parentNotificationId,
        string? idempotencyKey,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(
            order.Id,
            order.BuyerId,
            kind,
            destinationNumber,
            body,
            contactNumberId,
            parentNotificationId,
            idempotencyKey,
            sendAt);

        notification = await _notifications.AddAsync(notification, cancellationToken);

        try
        {
            var sent = await _messaging.SendAsync(new SendProviderMessageRequest(destinationNumber, body, sendAt), cancellationToken);
            notification.RecordProviderAccepted(sent.Sid, sent.Status ?? "queued");
            if (sent.ErrorCode.HasValue || !string.IsNullOrEmpty(sent.ErrorMessage) || sent.Body is not null)
            {
                notification.ApplyProviderState(sent.Status ?? notification.ProviderStatus, sent.ErrorCode, sent.ErrorMessage, sent.Body);
            }
        }
        catch (Exception)
        {
            notification.RecordLocalSendFailure("The provider did not accept the message.");
            _logger.LogWarning("Provider send failed for notification {NotificationId} on order {OrderId}.", notification.Id, order.Id);
        }

        await _notifications.UpdateAsync(notification, cancellationToken);
        return notification;
    }

    private async Task NotifyAsync(Order order, OrderNotificationKind kind, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        var contacts = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (contacts.Count == 0)
        {
            return;
        }

        var body = BuildBody(order, kind);
        foreach (var contact in contacts)
        {
            await SendToDestinationAsync(
                order,
                kind,
                contact.CanonicalNumber,
                contact.Id,
                body,
                parentNotificationId: null,
                idempotencyKey: null,
                sendAt,
                cancellationToken);
        }
    }

    public static string BuildBody(Order order, OrderNotificationKind kind)
    {
        return kind switch
        {
            OrderNotificationKind.OrderPlaced =>
                $"eShopOnWeb: Your order #{order.Id} has been placed. Total: {order.Total():0.00}.",
            OrderNotificationKind.OrderDispatched =>
                $"eShopOnWeb: Your order #{order.Id} is on its way.",
            OrderNotificationKind.DeliveryFollowUp =>
                $"eShopOnWeb: How did the delivery of order #{order.Id} go? We'd like to hear how it went.",
            OrderNotificationKind.OrderCancelled =>
                $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            _ => $"eShopOnWeb: An update is available for order #{order.Id}."
        };
    }
}
