using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
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

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemInput> items,
        Address shippingAddress, System.Threading.CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException();
        }

        // Consolidate duplicate lines so the same catalog item requested twice becomes one order line.
        var requestedQuantities = items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        if (requestedQuantities.Values.Any(q => q <= 0))
        {
            throw new ArgumentException("Order item quantities must be greater than zero.", nameof(items));
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(requestedQuantities.Keys.ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, cancellationToken);

        var missing = requestedQuantities.Keys.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Catalog item(s) not found: {string.Join(", ", missing)}", nameof(items));
        }

        // Prices come from the catalog, never from the caller.
        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestedQuantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
