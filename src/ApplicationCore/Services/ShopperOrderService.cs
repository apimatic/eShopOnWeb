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
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly OrderNotificationCoordinator _notificationCoordinator;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderNotificationCoordinator notificationCoordinator)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _notificationCoordinator = notificationCoordinator;
    }

    public async Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(request =>
        {
            if (request.Quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Quantity must be greater than zero.");
            }

            var catalogItem = catalogItems.First(c => c.Id == request.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, request.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notificationCoordinator.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> ListMineAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByBuyerIdSpec(orders.Select(o => o.Id)),
            cancellationToken);

        await _notificationCoordinator.RefreshAsync(notifications, cancellationToken);

        return orders
            .Select(order => new ShopperOrderSummary
            {
                Order = order,
                Notifications = notifications.Where(n => n.OrderId == order.Id).ToList()
            })
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!isAdministrator && order.BuyerId != buyerId))
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        await _notificationCoordinator.RefreshAsync(notifications, cancellationToken);
        return notifications;
    }
}
