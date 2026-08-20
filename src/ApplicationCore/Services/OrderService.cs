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
    private static readonly Address DefaultShipToAddress =
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

    public async Task<Order> CreateOrderFromCatalogItemsAsync(string buyerId, IReadOnlyList<OrderCatalogItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("An order must contain at least one catalog item.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids));

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            if (item.Quantity < 1)
            {
                throw new ArgumentException("Quantity must be at least 1.", nameof(items));
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem is null)
            {
                throw new CatalogItemNotFoundException(item.CatalogItemId);
            }

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = catalogItem.PictureUri;
            }
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "images/products/placeholder.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        return await _orderRepository.AddAsync(order);
    }

    public async Task<Order> DispatchOrderAsync(int orderId)
    {
        var order = await GetRequiredOrder(orderId);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId)
    {
        var order = await GetRequiredOrder(orderId);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    private async Task<Order> GetRequiredOrder(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }
}
