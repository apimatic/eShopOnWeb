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

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly OrderNotificationDispatcher _dispatcher;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderNotificationDispatcher dispatcher)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _dispatcher = dispatcher;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));

        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            Guard.Against.NegativeOrZero(line.CatalogItemId, nameof(line.CatalogItemId));
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _dispatcher.NotifyAsync(
            order.Id,
            buyerId,
            NotificationKind.OrderPlaced,
            OrderNotificationDispatcher.PlacedBody(order.Id),
            sendAt: null,
            parentNotificationId: null,
            idempotencyKey: null,
            destinationOverride: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _dispatcher.RefreshAsync(notification, cancellationToken);
        }

        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>?> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _dispatcher.RefreshAsync(notification, cancellationToken);
        }

        return notifications;
    }
}
