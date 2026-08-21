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

public class OrderNotificationDispatcher
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IAppLogger<OrderNotificationDispatcher> _logger;

    public OrderNotificationDispatcher(
        ITwilioMessagingClient messagingClient,
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        IAppLogger<OrderNotificationDispatcher> logger)
    {
        _messagingClient = messagingClient;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task NotifyAsync(Order order, NotificationKind kind, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
            if (destinations.Count == 0)
            {
                return;
            }

            var body = BodyFor(kind, order.Id);
            foreach (var destination in destinations)
            {
                await SendAndRecordAsync(order, kind, destination.PhoneNumber, body, sendAt, sourceNotificationId: null, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to send {Kind} notification for order {OrderId}.", kind, order.Id);
        }
    }

    public async Task<OrderNotification> SendResendAsync(OrderNotification original, CancellationToken cancellationToken)
    {
        return await SendAndRecordAsync(
            orderId: original.OrderId,
            buyerId: original.BuyerId,
            kind: original.Kind,
            destinationNumber: original.DestinationNumber,
            body: original.Body,
            sendAt: null,
            sourceNotificationId: original.Id,
            cancellationToken);
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var pending = await _notifications.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelAsync(notification, cancellationToken);
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
                var snapshot = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
                if (snapshot?.Status is null)
                {
                    continue;
                }

                notification.ApplyProviderState(snapshot.Status, snapshot.ErrorCode, snapshot.Sid);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to refresh provider status for notification {NotificationId}.", notification.Id);
            }
        }
    }

    public async Task TryCancelAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            return;
        }

        try
        {
            var current = await _messagingClient.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            if (current?.Status is not null)
            {
                notification.ApplyProviderState(current.Status, current.ErrorCode, current.Sid);
            }

            if (!string.Equals(notification.ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                await _notifications.UpdateAsync(notification, cancellationToken);
                return;
            }

            var cancelled = await _messagingClient.CancelAsync(notification.ProviderMessageSid, cancellationToken);
            if (cancelled?.Status is not null)
            {
                notification.ApplyProviderState(cancelled.Status, cancelled.ErrorCode, cancelled.Sid);
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to cancel follow-up notification {NotificationId} for order {OrderId}.", notification.Id, notification.OrderId);
        }
    }

    private async Task<OrderNotification> SendAndRecordAsync(
        Order order,
        NotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        return await SendAndRecordAsync(order.Id, order.BuyerId, kind, destinationNumber, body, sendAt, sourceNotificationId, cancellationToken);
    }

    private async Task<OrderNotification> SendAndRecordAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? sendAt,
        int? sourceNotificationId,
        CancellationToken cancellationToken)
    {
        TwilioSendResult result;
        try
        {
            result = await _messagingClient.SendAsync(new TwilioSendMessageRequest
            {
                To = destinationNumber,
                Body = body,
                SendAt = sendAt
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Provider send failed for {Kind} on order {OrderId}.", kind, orderId);
            result = new TwilioSendResult { Accepted = false, ErrorStatus = "failed" };
        }

        var status = result.Message?.Status
            ?? result.ErrorStatus
            ?? "failed";
        var notification = new OrderNotification(
            orderId,
            buyerId,
            kind,
            destinationNumber,
            body,
            result.Message?.Sid,
            status,
            result.Message?.ErrorCode ?? result.ErrorCode,
            sendAt,
            sourceNotificationId);

        return await _notifications.AddAsync(notification, cancellationToken);
    }

    public static string BodyFor(NotificationKind kind, int orderId)
    {
        return kind switch
        {
            NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you.",
            NotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{orderId} is on its way.",
            NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{orderId} go?",
            NotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled.",
            _ => $"eShopOnWeb: An update on order #{orderId}."
        };
    }
}
