using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperCheckoutService : IShopperCheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public ShopperCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one catalog item.");
        }

        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new PaymentValidationException("Catalog item id must be a positive integer.");
            }

            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException("Quantity must be greater than zero.");
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo ?? new Address("123 Main Street", "Seattle", "WA", "US", "98101");
        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
