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

public class CatalogOrderService : ICatalogOrderService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public CatalogOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Each item quantity must be greater than zero.", nameof(items));
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.", nameof(items));
        }

        var orderItems = items.Select(requestItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requestItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestItem.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _orderRepository.GetByIdAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
