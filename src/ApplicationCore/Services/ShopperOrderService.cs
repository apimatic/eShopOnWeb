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
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must include at least one catalog item.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new ArgumentException("Each order line must include a catalog item id and a positive quantity.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("A cancelled order cannot be dispatched.");
        }

        var alreadyDispatched = order.Status == OrderStatus.Dispatched;
        if (!alreadyDispatched)
        {
            order.MarkDispatched();
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        if (!alreadyDispatched)
        {
            await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        }

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        var alreadyCancelled = order.Status == OrderStatus.Cancelled;
        if (!alreadyCancelled)
        {
            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        }

        return order;
    }

    public Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException("Order not found.");
        }

        return order;
    }
}
