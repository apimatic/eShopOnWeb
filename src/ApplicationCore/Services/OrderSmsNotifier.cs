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

public class OrderSmsNotifier
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IAppLogger<OrderSmsNotifier> _logger;

    public OrderSmsNotifier(
        IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        ITwilioMessagingClient messagingClient,
        IAppLogger<OrderSmsNotifier> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var body = $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you for shopping with us.";
        return await SendToShopperAsync(order, NotificationKind.OrderPlaced, body, scheduledFor: null, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default)
    {
        var dispatchedBody = $"eShopOnWeb: Your order #{order.Id} is on its way.";
        var sent = (await SendToShopperAsync(order, NotificationKind.OrderDispatched, dispatchedBody, scheduledFor: null, cancellationToken)).ToList();

        var followUpBody = $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love to hear from you.";
        var sendAt = DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay);
        sent.AddRange(await SendToShopperAsync(order, NotificationKind.DeliveryFollowUp, followUpBody, sendAt, cancellationToken));
        return sent;
    }

    public async Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default)
    {
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        var body = $"eShopOnWeb: Your order #{order.Id} has been cancelled.";
        return await SendToShopperAsync(order, NotificationKind.OrderCancelled, body, scheduledFor: null, cancellationToken);
    }

    public async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var followUps = await _notifications.ListAsync(new PendingFollowUpsByOrderIdSpec(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            await RefreshFromProviderAsync(followUp, cancellationToken);
            if (!followUp.IsPendingWithProvider || !followUp.HasProviderIdentity)
            {
                continue;
            }

            try
            {
                var updated = await _messagingClient.CancelMessageAsync(followUp.ProviderMessageSid!, cancellationToken);
                if (updated is not null)
                {
                    followUp.RecordProviderResult(updated.Sid, updated.Status, updated.ErrorCode, updated.ErrorMessage);
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                }
            }
            catch
            {
                _logger.LogWarning("Failed to cancel scheduled follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
        }
    }

    public async Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        if (!notification.HasProviderIdentity)
        {
            return;
        }

        try
        {
            var snapshot = await _messagingClient.FetchMessageAsync(notification.ProviderMessageSid!, cancellationToken);
            if (snapshot is null)
            {
                return;
            }

            notification.RecordProviderResult(snapshot.Sid, snapshot.Status, snapshot.ErrorCode, snapshot.ErrorMessage);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch
        {
            _logger.LogWarning("Failed to refresh provider status for notification {NotificationId}.", notification.Id);
        }
    }

    public async Task RefreshAllAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            await RefreshFromProviderAsync(notification, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<OrderNotification>> SendToShopperAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken)
    {
        var destinations = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
        if (destinations.Count == 0)
        {
            return Array.Empty<OrderNotification>();
        }

        var created = new List<OrderNotification>();
        foreach (var destination in destinations)
        {
            var notification = new OrderNotification(
                order.Id,
                order.BuyerId,
                destination.Id,
                destination.CanonicalNumber,
                kind,
                body,
                scheduledFor);

            notification = await _notifications.AddAsync(notification, cancellationToken);

            try
            {
                TwilioMessageSnapshot? result = scheduledFor.HasValue
                    ? await _messagingClient.ScheduleSmsAsync(destination.CanonicalNumber, body, scheduledFor.Value, cancellationToken)
                    : await _messagingClient.SendSmsAsync(destination.CanonicalNumber, body, cancellationToken);

                if (result is null)
                {
                    notification.RecordSendFailure();
                }
                else
                {
                    notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage);
                }
            }
            catch
            {
                _logger.LogWarning("SMS notification {NotificationId} for order {OrderId} could not be handed to the provider.", notification.Id, order.Id);
                notification.RecordSendFailure();
            }

            await _notifications.UpdateAsync(notification, cancellationToken);
            created.Add(notification);
        }

        return created;
    }
}
