using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);
    }

    public async Task<Order> CreateOrderFromCatalogItemsAsync(
        string buyerId,
        IReadOnlyCollection<CatalogQuantity> items,
        Address? shippingAddress = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));

        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        foreach (var item in items)
        {
            Guard.Against.OutOfRange(item.CatalogItemId, nameof(item.CatalogItemId), 1, int.MaxValue);
            Guard.Against.OutOfRange(item.Quantity, nameof(item.Quantity), 1, int.MaxValue);
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = catalogIds.First(id => !found.Contains(id));
            throw new ArgumentException($"Catalog item {missing} was not found.", nameof(items));
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress ?? DefaultShippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderMutationResult?> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }

        var changed = order.MarkDispatched();
        if (changed)
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        return new OrderMutationResult(order, changed);
    }

    public async Task<OrderMutationResult?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            return null;
        }

        var changed = order.MarkCancelled();
        if (changed)
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        return new OrderMutationResult(order, changed);
    }
}
