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

    public const int MaxUnitsPerLine = 1000;

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shippingAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));

        if (lines is null || lines.Count == 0)
            throw new OrderValidationException("An order needs at least one item.");
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > MaxUnitsPerLine))
            throw new OrderValidationException($"Each quantity must be between 1 and {MaxUnitsPerLine}.");

        // The same catalog item listed twice is one order line.
        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (quantities.Values.Any(q => q > MaxUnitsPerLine))
            throw new OrderValidationException($"Each quantity must be between 1 and {MaxUnitsPerLine}.");

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()));
        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).OrderBy(id => id).ToList();
        if (missing.Count > 0)
            throw new OrderValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = catalogItems
            .OrderBy(c => c.Id)
            .Select(catalogItem =>
            {
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
                return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
            }).ToList();

        var order = new Order(buyerId, shippingAddress, items);

        return await _orderRepository.AddAsync(order);
    }
}
