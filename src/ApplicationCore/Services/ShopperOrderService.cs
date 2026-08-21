using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
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

    public async Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        foreach (var item in request.Items)
        {
            if (item.Quantity < 1)
            {
                throw new ArgumentException("Quantity must be at least 1.");
            }
        }

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = request.Items.Select(requestItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requestItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestItem.Quantity);
        }).ToList();

        var order = new Order(request.BuyerId, request.ShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _dispatcher.NotifyAsync(order, NotificationKind.OrderPlaced, sendAt: null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        await _dispatcher.RefreshAsync(notifications, cancellationToken);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => new ShopperOrderSummary
            {
                Order = order,
                Notifications = notifications.Where(n => n.OrderId == order.Id).ToList()
            })
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await _dispatcher.RefreshAsync(notifications, cancellationToken);
        return notifications;
    }
}
