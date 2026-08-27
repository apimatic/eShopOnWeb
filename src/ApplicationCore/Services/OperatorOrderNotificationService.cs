using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderNotificationService : IOperatorOrderNotificationService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly OrderNotificationCoordinator _notificationCoordinator;

    public OperatorOrderNotificationService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        OrderNotificationCoordinator notificationCoordinator)
    {
        _orders = orders;
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _notificationCoordinator = notificationCoordinator;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        await _notificationCoordinator.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        await _notificationCoordinator.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notifications.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpec(notificationId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (source.ContentDisposed || string.IsNullOrEmpty(source.Body))
        {
            throw new InvalidOperationException("The original message content is no longer available to resend.");
        }

        var destinationStillRegistered = source.ContactNumberId is int contactId
            && await _contactNumbers.GetByIdAsync(contactId, cancellationToken) is { } contact
            && contact.BuyerId == source.BuyerId
            && contact.PhoneNumber == source.DestinationNumber;

        if (!destinationStillRegistered)
        {
            var currentNumbers = await _contactNumbers.ListAsync(
                new ContactNumbersByBuyerIdSpec(source.BuyerId),
                cancellationToken);
            destinationStillRegistered = currentNumbers.Any(c => c.PhoneNumber == source.DestinationNumber);
        }

        if (!destinationStillRegistered)
        {
            throw new InvalidOperationException("The destination contact number is no longer on file and cannot be messaged.");
        }

        return await _notificationCoordinator.SendResendAsync(
            source,
            source.DestinationNumber,
            source.ContactNumberId,
            idempotencyKey,
            cancellationToken);
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!notification.ContentDisposed && !string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messagingClient.RedactBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var providerMessages = await _messagingClient.ListFromNumberAsync(from, to, cancellationToken);
        var localInRange = await _notifications.ListAsync(new OrderNotificationsInRangeSpec(from, to), cancellationToken);

        var providerSids = providerMessages
            .Select(m => m.Sid)
            .Where(sid => !string.IsNullOrEmpty(sid))
            .Cast<string>()
            .Distinct()
            .ToArray();

        var localsBySid = new Dictionary<string, OrderNotification>(StringComparer.Ordinal);
        foreach (var notification in localInRange)
        {
            if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
            {
                localsBySid[notification.ProviderMessageSid] = notification;
            }
        }

        if (providerSids.Length > 0)
        {
            var localsByProviderSid = await _notifications.ListAsync(
                new OrderNotificationsByProviderSidsSpec(providerSids),
                cancellationToken);
            foreach (var notification in localsByProviderSid)
            {
                if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
                {
                    localsBySid[notification.ProviderMessageSid] = notification;
                }
            }
        }

        var matched = new List<ReconciliationMatch>();
        var providerOnly = new List<SmsMessageSnapshot>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var providerMessage in providerMessages)
        {
            if (!string.IsNullOrEmpty(providerMessage.Sid)
                && localsBySid.TryGetValue(providerMessage.Sid, out var notification))
            {
                matched.Add(new ReconciliationMatch
                {
                    Notification = notification,
                    ProviderMessage = providerMessage
                });
                matchedSids.Add(providerMessage.Sid);
            }
            else
            {
                providerOnly.Add(providerMessage);
            }
        }

        var eshopOnly = localInRange
            .Where(n => string.IsNullOrEmpty(n.ProviderMessageSid) || !matchedSids.Contains(n.ProviderMessageSid))
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FromNumber = _messagingClient.FromNumber,
            Matched = matched,
            ProviderOnly = providerOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");
    }
}
