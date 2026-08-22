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
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;

    public ShopperOrderService(
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

    public async Task<Order> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new EmptyBasketOnCheckoutException("An order must contain at least one catalog item.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
                throw new InvalidOperationException($"Catalog item {line.CatalogItemId} was not found.");

            var snapshot = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMineAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
                    ?? throw new ShopperOrderNotFoundException(orderId);

        if (order.TryMarkDispatched())
        {
            await _orders.UpdateAsync(order, cancellationToken);
            await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        }

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
                    ?? throw new ShopperOrderNotFoundException(orderId);

        if (order.TryMarkCancelled())
        {
            await _orders.UpdateAsync(order, cancellationToken);
            await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        }

        return order;
    }

    public async Task<Order?> GetByIdForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null;
        return order;
    }
}
