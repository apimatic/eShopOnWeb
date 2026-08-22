using System.Collections.Generic;
using System.Linq;
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

    public async Task<Order> CreateOrderFromCatalogAsync(string buyerId, IReadOnlyList<CatalogOrderLine> items, Address shippingAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));

        var mergedLines = items
            .Where(item => item.Quantity > 0 && item.CatalogItemId > 0)
            .GroupBy(item => item.CatalogItemId)
            .Select(group => new CatalogOrderLine(group.Key, group.Sum(item => item.Quantity)))
            .ToList();

        Guard.Against.NullOrEmpty(mergedLines, nameof(items));

        var catalogItemsSpecification = new CatalogItemsSpecification(mergedLines.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        if (catalogItems.Count != mergedLines.Count)
        {
            var foundIds = catalogItems.Select(item => item.Id).ToHashSet();
            var missing = mergedLines.Select(item => item.CatalogItemId).Where(id => !foundIds.Contains(id));
            throw new KeyNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = mergedLines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order);
    }
}
