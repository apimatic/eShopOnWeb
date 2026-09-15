using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    private static readonly Address DefaultShipTo = new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;

    public OrderPlacementService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
    }

    public async Task<Order> PlaceAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipTo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.", nameof(items));
        }

        var normalized = items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new PlaceOrderItem(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (normalized.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item with a positive quantity.", nameof(items));
        }

        var ids = normalized.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new KeyNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = normalized.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);
        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        return order;
    }
}
