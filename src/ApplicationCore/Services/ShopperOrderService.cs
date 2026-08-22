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

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (items.Count == 0)
        {
            throw new OrderStateException("An order must contain at least one catalog item.");
        }

        if (items.Any(i => i.Quantity < 1 || i.CatalogItemId < 1))
        {
            throw new OrderStateException("Each item must include a catalog item id and a quantity of at least 1.");
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new EntityNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notifications.NotifyOrderPlacedAsync(new OrderNotificationDispatchContext
        {
            OrderId = order.Id,
            BuyerId = buyerId
        }, cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        try
        {
            order.MarkDispatched();
        }
        catch (System.InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _notifications.NotifyOrderDispatchedAsync(new OrderNotificationDispatchContext
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId
        }, cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        try
        {
            order.MarkCancelled();
        }
        catch (System.InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _notifications.NotifyOrderCancelledAsync(new OrderNotificationDispatchContext
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId
        }, cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new OrdersByBuyerIdSpec(buyerId), cancellationToken);
    }

    public async Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        return order;
    }
}
