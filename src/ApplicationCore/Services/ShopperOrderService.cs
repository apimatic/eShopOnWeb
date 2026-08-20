using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static Address CreateDefaultShipTo() =>
        new("123 Main St.", "Seattle", "WA", "United States", "98101");

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

    public async Task<Order> PlaceAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new KeyNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, CreateDefaultShipTo(), orderItems);
        order = await _orders.AddAsync(order, cancellationToken);
        await TryNotify(() => _notifications.NotifyOrderPlacedAsync(order, cancellationToken), order.Id, "placed");
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.SaveChangesAsync(cancellationToken);
        await TryNotify(() => _notifications.NotifyOrderDispatchedAsync(order, cancellationToken), order.Id, "dispatched");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await RequireOrder(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.SaveChangesAsync(cancellationToken);
        await TryNotify(() => _notifications.NotifyOrderCancelledAsync(order, cancellationToken), order.Id, "cancelled");
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    private async Task<Order> RequireOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }

    private async Task TryNotify(Func<Task> notify, int orderId, string action)
    {
        try
        {
            await notify();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} {Action} succeeded; notification failed ({Exception}).", orderId, action, ex.GetType().Name);
        }
    }
}
