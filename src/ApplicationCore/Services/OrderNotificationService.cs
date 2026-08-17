using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationService : IOrderNotificationService
{
    /// <summary>How far ahead the delivery follow-up is queued with the provider.</summary>
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ISmsProvider _provider;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<ContactNumber> contactNumbers,
        IRepository<SmsNotification> notifications,
        INotificationDispatcher dispatcher,
        ISmsProvider provider,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _dispatcher = dispatcher;
        _provider = provider;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ArgumentException("Order line quantities must be greater than zero.", nameof(lines));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order {order.Id} for a buyer with {items.Count} line(s).");

        await NotifyBuyerAsync(order, NotificationKind.OrderPlaced, scheduleAt: null, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        _logger.LogInformation($"Dispatching order {order.Id}.");

        // Tell the shopper it is on its way now...
        await NotifyBuyerAsync(order, NotificationKind.OrderDispatched, scheduleAt: null, cancellationToken);

        // ...and queue the delivery follow-up with the provider for a few days later.
        await NotifyBuyerAsync(order, NotificationKind.DeliveryFollowUp, scheduleAt: DateTimeOffset.UtcNow.Add(FollowUpDelay), cancellationToken);

        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        _logger.LogInformation($"Cancelling order {order.Id}.");

        // Call off any follow-up that has not gone out BEFORE telling the shopper it is cancelled, so a
        // "how did delivery go?" for a cancelled order can never reach them.
        await CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await NotifyBuyerAsync(order, NotificationKind.OrderCancelled, scheduleAt: null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithNotifications>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notifications.ListAsync(new SmsNotificationsByOrderIdsSpecification(orderIds), cancellationToken);
        await _dispatcher.RefreshManyAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<SmsNotification>)g.ToList());
        return orders
            .Select(o => new OrderWithNotifications(o, byOrder.TryGetValue(o.Id, out var ns) ? ns : Array.Empty<SmsNotification>()))
            .ToList();
    }

    public async Task<OrderWithNotifications?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Do not reveal another shopper's order even by its existence.
            return null;
        }

        var notifications = await _notifications.ListAsync(new SmsNotificationsByOrderSpecification(orderId), cancellationToken);
        await _dispatcher.RefreshManyAsync(notifications, cancellationToken);
        return new OrderWithNotifications(order, notifications);
    }

    private async Task NotifyBuyerAsync(Order order, NotificationKind kind, DateTimeOffset? scheduleAt, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(order.BuyerId), cancellationToken);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged.
            _logger.LogInformation($"No contact number on file for the buyer of order {order.Id}; not sending {kind}.");
            return;
        }

        var body = NotificationMessages.For(kind, order);
        foreach (var number in numbers)
        {
            var notification = new SmsNotification(order.Id, order.BuyerId, number.PhoneNumber, body, kind, scheduleAt);
            await _dispatcher.SendNewAsync(notification, cancellationToken);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new ScheduledFollowUpsByOrderSpecification(orderId), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (followUp.ProviderMessageId is null)
            {
                continue;
            }

            try
            {
                // Cancellation goes through the provider; success flips our mirror to canceled.
                var canceled = await _provider.CancelScheduledAsync(followUp.ProviderMessageId, cancellationToken);
                if (canceled)
                {
                    followUp.MarkCanceled();
                    await _notifications.UpdateAsync(followUp, cancellationToken);
                    _logger.LogInformation($"Called off scheduled follow-up id {followUp.Id} for order {orderId}.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not cancel scheduled follow-up id {followUp.Id}: {ex.Message}");
            }
        }
    }
}
