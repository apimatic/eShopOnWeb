using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    // The follow-up is queued with the provider this many days after dispatch.
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // Provider statuses beyond which no further change is expected.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled"
    };

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ISmsService _smsService;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<CatalogItem> itemRepository,
        ISmsService smsService,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _itemRepository = itemRepository;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new InvalidOrderException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new InvalidOrderException("Item quantities must be positive.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray()), ct);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem is null)
            {
                throw new InvalidOrderException($"Catalog item {item.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        await NotifyBuyerAsync(order, NotificationKind.OrderPlaced,
            $"eShopOnWeb: your order #{order.Id} has been placed. Total: ${order.Total():0.00}. Thank you for shopping with us!", ct);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, ct);

        await NotifyBuyerAsync(order, NotificationKind.OrderDispatched,
            $"eShopOnWeb: good news — your order #{order.Id} is on its way.", ct);

        // The follow-up is queued with the provider itself (scheduled send), not held
        // in this application — so it goes out even if this app is down, and can be
        // called off at the provider if the order is cancelled.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyBuyerAsync(order, NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: your order #{order.Id} should have arrived by now — how did the delivery go?",
            ct, scheduleAt: sendAt);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        // Call off any follow-up that has not yet gone out — a cancelled order must
        // never produce a "how did the delivery go" message.
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        foreach (var followUp in notifications.Where(n =>
                     n.Kind == NotificationKind.DeliveryFollowUp &&
                     n.MessageSid != null &&
                     !TerminalStatuses.Contains(n.Status)))
        {
            var cancelResult = await _smsService.CancelScheduledAsync(followUp.MessageSid!, ct);
            if (cancelResult.Succeeded)
            {
                followUp.UpdateStatus(cancelResult.Status ?? "canceled", cancelResult.ErrorCode, cancelResult.ErrorMessage);
            }
            else
            {
                _logger.LogWarning("Could not cancel scheduled follow-up {messageSid} for order {orderId}: {error}",
                    followUp.MessageSid, orderId, cancelResult.ErrorMessage ?? "unknown");
            }
            await _notificationRepository.UpdateAsync(followUp, ct);
        }

        await NotifyBuyerAsync(order, NotificationKind.OrderCancelled,
            $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.", ct);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);

        foreach (var order in orders)
        {
            var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), ct);
            await RefreshStatusesAsync(notifications, ct);
        }

        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(int orderId, CancellationToken ct = default)
    {
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), ct);
        await RefreshStatusesAsync(notifications, ct);
        return notifications;
    }

    public async Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            return new ResendResult(existing, IdempotentReplay: true);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }
        if (original.ContentRedacted || original.Body is null)
        {
            throw new OrderStateException($"Notification {notificationId} content has been disposed of and cannot be re-sent.");
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            NotificationKind.Resend, original.Body, idempotencyKey: idempotencyKey,
            resendOfNotificationId: original.Id);

        var result = await _smsService.SendAsync(resend.ToNumber, resend.Body!, ct);
        ApplySendResult(resend, result);

        resend = await _notificationRepository.AddAsync(resend, ct);
        return new ResendResult(resend, IdempotentReplay: false);
    }

    public async Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            throw new NotificationNotFoundException(notificationId);
        }

        if (!notification.ContentRedacted)
        {
            if (notification.MessageSid is not null)
            {
                // Dispose of the text at the provider, not merely in this application.
                // A provider failure surfaces to the operator and nothing is marked redacted.
                var result = await _smsService.RedactBodyAsync(notification.MessageSid, ct);
                if (!result.Succeeded)
                {
                    throw new SmsProviderException(
                        $"The provider could not dispose of the message content: {result.ErrorMessage}", null);
                }
            }

            notification.MarkContentRedacted();
            await _notificationRepository.UpdateAsync(notification, ct);
        }

        return notification;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var providerMessages = await _smsService.ListSentAsync(from, to, ct);
        var localNotifications = await _notificationRepository.ListAsync(
            new OrderNotificationsInRangeSpecification(from, to), ct);

        var localBySid = localNotifications
            .Where(n => n.MessageSid is not null)
            .GroupBy(n => n.MessageSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var providerOnly = new List<ReconciliationEntry>();

        foreach (var message in providerMessages)
        {
            if (localBySid.TryGetValue(message.Sid, out var local))
            {
                matched.Add(new ReconciliationEntry(local.Id, message.Sid, message.To, message.Status, message.DateSent));
            }
            else
            {
                providerOnly.Add(new ReconciliationEntry(null, message.Sid, message.To, message.Status, message.DateSent));
            }
        }

        var localOnly = localNotifications
            .Where(n => n.MessageSid is null || !providerMessages.Any(m => m.Sid == n.MessageSid))
            .Select(n => new ReconciliationEntry(n.Id, n.MessageSid, null, n.Status, n.CreatedAt))
            .ToList();

        return new ReconciliationReport(matched, providerOnly, localOnly);
    }

    private async Task NotifyBuyerAsync(Order order, NotificationKind kind, string body, CancellationToken ct,
        DateTimeOffset? scheduleAt = null)
    {
        var contactNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(order.BuyerId), ct);

        // A shopper with no number on file is simply not messaged.
        foreach (var contactNumber in contactNumbers)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contactNumber.PhoneNumber,
                kind, body, scheduledFor: scheduleAt);

            // A message that cannot be sent must never fail the underlying operation:
            // the failure is recorded on the notification, not thrown.
            var result = scheduleAt.HasValue
                ? await _smsService.ScheduleAsync(contactNumber.PhoneNumber, body, scheduleAt.Value, ct)
                : await _smsService.SendAsync(contactNumber.PhoneNumber, body, ct);

            ApplySendResult(notification, result);
            await _notificationRepository.AddAsync(notification, ct);
        }
    }

    private void ApplySendResult(OrderNotification notification, SmsSendResult result)
    {
        if (result.Succeeded && result.MessageSid is not null)
        {
            notification.MarkAccepted(result.MessageSid, result.Status);
        }
        else
        {
            notification.MarkSendFailed(result.ErrorCode, result.ErrorMessage);
            _logger.LogWarning("Notification of kind {kind} for order {orderId} could not be sent: {error}",
                notification.Kind, notification.OrderId, result.ErrorMessage ?? "unknown");
        }
    }

    private async Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct)
    {
        // There is no callback URL the provider can reach, so "what became of each
        // message" is learned by asking the provider. Best-effort: a refresh failure
        // leaves the last known status in place.
        foreach (var notification in notifications.Where(n =>
                     n.MessageSid != null && !TerminalStatuses.Contains(n.Status)))
        {
            var result = await _smsService.FetchAsync(notification.MessageSid!, ct);
            if (result.Succeeded && result.Status is not null)
            {
                notification.UpdateStatus(result.Status, result.ErrorCode, result.ErrorMessage);
                await _notificationRepository.UpdateAsync(notification, ct);
            }
        }
    }
}
