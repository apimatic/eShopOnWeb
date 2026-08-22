using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipTo = new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<ShopperOrderService> _logger;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications,
        IAppLogger<ShopperOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipTo, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.");
        }

        foreach (var item in items)
        {
            if (item.Quantity < 1)
            {
                throw new ArgumentException("Each order item must have a quantity of at least 1.");
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var catalogById = catalogItems.ToDictionary(c => c.Id);
        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}", order.Id, buyerId);

        await _notifications.NotifyOrderPlacedAsync(order.Id, buyerId, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken)
            ?? throw new NotFoundException("Order was not found.");

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new ConflictException("A cancelled order cannot be dispatched.");
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Dispatched order {OrderId}", order.Id);

        await _notifications.NotifyOrderDispatchedAsync(order.Id, order.BuyerId, cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken)
            ?? throw new NotFoundException("Order was not found.");

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new ConflictException("The order is already cancelled.");
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}", order.Id);

        await _notifications.NotifyOrderCancelledAsync(order.Id, order.BuyerId, cancellationToken);
    }

    public async Task<Order> GetOrderForShopperAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken)
            ?? throw new NotFoundException("Order was not found.");

        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new NotFoundException("Order was not found.");
        }

        return order;
    }
}
