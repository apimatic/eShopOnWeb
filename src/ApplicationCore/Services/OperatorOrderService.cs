using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderService : IOperatorOrderService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationIdempotencyRecord> _idempotency;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly OrderNotificationDispatcher _dispatcher;

    public OperatorOrderService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IRepository<NotificationIdempotencyRecord> idempotency,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        OrderNotificationDispatcher dispatcher)
    {
        _orders = orders;
        _notifications = notifications;
        _idempotency = idempotency;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _dispatcher = dispatcher;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _dispatcher.NotifyAsync(order, NotificationKind.OrderDispatched, sendAt: null, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(OrderNotificationDispatcher.FollowUpDelay);
        await _dispatcher.NotifyAsync(order, NotificationKind.DeliveryFollowUp, followUpAt, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await RequireOrder(orderId, cancellationToken);
        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        await _dispatcher.CancelPendingFollowUpsAsync(order.Id, cancellationToken);
        await _dispatcher.NotifyAsync(order, NotificationKind.OrderCancelled, sendAt: null, cancellationToken);
        return order;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var existing = await _idempotency.FirstOrDefaultAsync(
            new NotificationIdempotencySpecification(notificationId, idempotencyKey.Trim()), cancellationToken);
        if (existing is not null)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous is not null)
            {
                return previous;
            }
        }

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        await _dispatcher.RefreshAsync(new[] { original }, cancellationToken);

        if (!original.DidNotReachShopper() && !string.IsNullOrEmpty(original.ProviderMessageSid))
        {
            throw new NotificationOperationException("Only messages that did not reach the shopper can be re-sent.");
        }

        if (original.ContentRedacted || string.IsNullOrWhiteSpace(original.Body))
        {
            throw new NotificationOperationException("The message content has been disposed of and cannot be re-sent.");
        }

        var stillRegistered = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(original.BuyerId, original.DestinationNumber), cancellationToken);
        if (stillRegistered is null)
        {
            throw new NotificationOperationException("The destination number is no longer on file for this shopper.");
        }

        var resent = await _dispatcher.SendResendAsync(original, cancellationToken);
        await _idempotency.AddAsync(new NotificationIdempotencyRecord(original.Id, idempotencyKey.Trim(), resent.Id), cancellationToken);
        return resent;
    }

    public async Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.ConfiguredFromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _messagingClient.ListSentFromAsync(fromNumber, from, to, cancellationToken);
        var eshopNotifications = await _notifications.ListAsync(new OrderNotificationsInRangeSpecification(from, to), cancellationToken);

        var providerSids = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .Select(m => m.Sid!)
            .Distinct()
            .ToArray();

        if (providerSids.Length > 0)
        {
            var extra = await _notifications.ListAsync(new OrderNotificationsByProviderSidsSpecification(providerSids), cancellationToken);
            var knownIds = eshopNotifications.Select(n => n.Id).ToHashSet();
            foreach (var notification in extra)
            {
                if (knownIds.Add(notification.Id))
                {
                    eshopNotifications.Add(notification);
                }
            }
        }

        var eshopBySid = eshopNotifications
            .Where(n => !string.IsNullOrEmpty(n.ProviderMessageSid))
            .GroupBy(n => n.ProviderMessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<TwilioMessageSnapshot>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var providerMessage in providerMessages)
        {
            if (string.IsNullOrEmpty(providerMessage.Sid) || !seenSids.Add(providerMessage.Sid))
            {
                continue;
            }

            if (eshopBySid.TryGetValue(providerMessage.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch { Notification = notification, ProviderMessage = providerMessage });
            }
            else
            {
                providerOnly.Add(providerMessage);
            }
        }

        var matchedSids = matched.Select(m => m.ProviderMessage.Sid).ToHashSet();
        var eshopOnly = eshopNotifications
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !matchedSids.Contains(n.ProviderMessageSid))
            .ToList();

        return new NotificationReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = fromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<Order> RequireOrder(int orderId, CancellationToken cancellationToken)
    {
        return await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");
    }
}
