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

    public async Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress,
        IReadOnlyDictionary<int, int> catalogItemQuantities, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(catalogItemQuantities, nameof(catalogItemQuantities));
        if (catalogItemQuantities.Count == 0)
        {
            throw new Exceptions.PaymentOperationException("An order must contain at least one item.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(catalogItemQuantities.Keys.ToArray()), cancellationToken);

        var missing = catalogItemQuantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new Exceptions.PaymentOperationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = catalogItems.Select(catalogItem =>
        {
            var quantity = catalogItemQuantities[catalogItem.Id];
            Guard.Against.NegativeOrZero(quantity, nameof(catalogItemQuantities));
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
