using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));

        if (items.Count == 0)
        {
            throw new ArgumentException("An order must include at least one catalog item.", nameof(items));
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Each order line must have a quantity greater than zero.", nameof(items));
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new KeyNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        return await _orderRepository.AddAsync(order);
    }

    public async Task<Order> DispatchAsync(int orderId)
    {
        var order = await GetRequiredAsync(orderId);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await GetRequiredAsync(orderId);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public Task<Order?> GetByIdAsync(int orderId)
    {
        return _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        return orders;
    }

    private async Task<Order> GetRequiredAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
