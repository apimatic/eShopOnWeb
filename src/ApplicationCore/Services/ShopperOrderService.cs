using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipTo =
        new("123 Main Street", "Seattle", "WA", "United States", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Entities.NotificationAggregate.OrderNotification> _notifications;
    private readonly OrderNotificationSender _notificationSender;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IRepository<Entities.NotificationAggregate.OrderNotification> notifications,
        OrderNotificationSender notificationSender)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
        _notificationSender = notificationSender;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipTo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new OrderNotificationException(400, "At least one catalog item is required.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new OrderNotificationException(400, "Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new OrderNotificationException(400, $"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notificationSender.NotifyPlacedAsync(order.Id, buyerId, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return System.Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpec(buyerId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _notificationSender.RefreshFromProviderAsync(notification, cancellationToken);
        }

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Entities.NotificationAggregate.OrderNotification>)g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new ShopperOrderSummary
            {
                Order = o,
                Notifications = byOrder.TryGetValue(o.Id, out var list) ? list : System.Array.Empty<Entities.NotificationAggregate.OrderNotification>()
            })
            .ToList();
    }
}
