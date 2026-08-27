using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, PictureUriOrPlaceholder(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);
    }

    public async Task<Order> CreateOrderFromCatalogItemsAsync(
        string buyerId,
        IReadOnlyCollection<CatalogQuantity> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));

        if (items is null || items.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("Order must contain at least one item.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item.Quantity < 1)
            {
                throw new ArgumentException("Each order item must have a quantity of at least 1.");
            }

            quantities[item.CatalogItemId] = quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()),
            cancellationToken);

        foreach (var catalogItemId in quantities.Keys)
        {
            if (catalogItems.All(c => c.Id != catalogItemId))
            {
                throw new CatalogItemNotFoundException(catalogItemId);
            }
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, PictureUriOrPlaceholder(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    private string PictureUriOrPlaceholder(string pictureUri)
    {
        if (string.IsNullOrWhiteSpace(pictureUri))
        {
            return "placeholder";
        }

        var composed = _uriComposer.ComposePicUri(pictureUri);
        return string.IsNullOrWhiteSpace(composed) ? "placeholder" : composed;
    }
}
