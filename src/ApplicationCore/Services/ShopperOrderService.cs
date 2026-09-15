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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultApiShipTo = new("1 eShop Way", "Redmond", "WA", "USA", "98052");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly OrderSmsDispatcher _smsDispatcher;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderSmsDispatcher smsDispatcher)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _smsDispatcher = smsDispatcher;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} was not found.");

            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);

            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultApiShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _smsDispatcher.NotifyOrderAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return System.Array.Empty<ShopperOrderSummary>();
        }

        var notifications = await _notifications.ListAsync(
            new OrderNotificationsByOrderIdsSpecification(orders.Select(o => o.Id)),
            cancellationToken);

        await _smsDispatcher.RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .Select(o => new ShopperOrderSummary(
                o,
                byOrder.TryGetValue(o.Id, out var n) ? n : System.Array.Empty<OrderNotification>()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!isAdministrator && order.BuyerId != buyerId))
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await _smsDispatcher.RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }
}
