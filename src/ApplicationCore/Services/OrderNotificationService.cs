using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far in the future the "how did delivery go?" follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    // The API carries only catalog item ids and quantities. Orders placed this way get a placeholder
    // ship-to address so the existing order model's required address is satisfied.
    private static Address DefaultShippingAddress() => new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<Notification> notificationRepository,
        ISmsGateway smsGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _smsGateway = smsGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new OrderCreationException("An order must contain at least one item.");
        }

        var requestedIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new OrderCreationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new OrderCreationException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShippingAddress(), items);
        await _orderRepository.AddAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderPlaced,
            $"eShop order #{order.Id} placed — thank you! We'll text you as it moves.", cancellationToken);

        return order;
    }

    public async Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        order.Dispatch();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await NotifyBuyerAsync(order, NotificationType.OrderDispatched,
            $"eShop order #{order.Id} is on its way!", cancellationToken);

        // Queue the follow-up with the provider for a few days later — not held here on a timer.
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await NotifyBuyerAsync(order, NotificationType.DeliveryFollowUp,
            $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.",
            cancellationToken, scheduledSendAt: sendAt);

        return true;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Call off any not-yet-sent follow-up so a "how did delivery go?" text can never reach a
        // customer whose order was cancelled.
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledFollowUpForOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in scheduled)
        {
            if (!string.IsNullOrEmpty(followUp.ProviderMessageId))
            {
                try
                {
                    await _smsGateway.CancelScheduledAsync(followUp.ProviderMessageId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to cancel scheduled follow-up {0} for order {1}: {2}",
                        followUp.ProviderMessageId, orderId, ex.Message);
                }
            }
            followUp.MarkCanceled();
            await _notificationRepository.UpdateAsync(followUp, cancellationToken);
        }

        await NotifyBuyerAsync(order, NotificationType.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0) return Array.Empty<OrderWithNotifications>();

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);

        var byOrder = notifications.ToLookup(n => n.OrderId);
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder[o.Id].ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null) return null; // not the caller's order, or no such order

        var notifications = await _notificationRepository.ListAsync(
            new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshDeliveryStateAsync(notifications, cancellationToken);
        return notifications;
    }

    /// <summary>
    /// Sends a message about an order to every number the buyer has on file. A shopper with no number
    /// is simply not messaged, and a message that cannot be sent never fails the calling operation.
    /// </summary>
    private async Task NotifyBuyerAsync(Order order, NotificationType type, string body,
        CancellationToken cancellationToken, DateTimeOffset? scheduledSendAt = null)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0) return;

        var isScheduled = scheduledSendAt.HasValue;
        foreach (var number in numbers)
        {
            var notification = new Notification(order.Id, order.BuyerId, number.PhoneNumber, type, body,
                isScheduledFollowUp: isScheduled, scheduledSendAt: scheduledSendAt);
            await _notificationRepository.AddAsync(notification, cancellationToken);

            await TrySendAsync(notification, cancellationToken);
        }
    }

    /// <summary>Attempts the provider send and records the outcome. Never throws.</summary>
    private async Task TrySendAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _smsGateway.SendAsync(
                new SendSmsRequest(notification.ToNumber, notification.Body!, notification.ScheduledSendAt),
                cancellationToken);
            notification.RecordAccepted(result.Sid, result.Status, result.ErrorCode);
        }
        catch (Exception ex)
        {
            notification.RecordSendFailure();
            _logger.LogWarning("SMS send failed for notification {0} (order {1}, type {2}): {3}",
                notification.Id, notification.OrderId, notification.Type, ex.Message);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
    }

    /// <summary>Refreshes each non-terminal notification's delivery outcome from the provider. Best-effort.</summary>
    private async Task RefreshDeliveryStateAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            if (string.IsNullOrEmpty(notification.ProviderMessageId) || NotificationStatus.IsTerminal(notification.Status))
            {
                continue;
            }

            try
            {
                var current = await _smsGateway.FetchAsync(notification.ProviderMessageId, cancellationToken);
                if (!string.Equals(current.Status, notification.Status, StringComparison.OrdinalIgnoreCase)
                    || current.ErrorCode != notification.ErrorCode)
                {
                    notification.UpdateDeliveryState(current.Status, current.ErrorCode);
                    await _notificationRepository.UpdateAsync(notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to refresh delivery state for notification {0}: {1}",
                    notification.Id, ex.Message);
            }
        }
    }
}
