using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        Address shipToAddress,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        // Fold duplicate lines so stock is checked against the combined quantity per item.
        var requested = new Dictionary<int, int>();
        foreach (var (catalogItemId, quantity) in items)
        {
            if (quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {catalogItemId} must be greater than zero.", nameof(items));
            }
            requested[catalogItemId] = requested.TryGetValue(catalogItemId, out var existing) ? existing + quantity : quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(requested.Keys.ToArray()), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        // First pass: every id must exist and hold enough. Mutate nothing until all lines pass,
        // so a rejected order leaves eShop's levels untouched.
        foreach (var (catalogItemId, quantity) in requested)
        {
            if (!catalogById.TryGetValue(catalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {catalogItemId} does not exist.", nameof(items));
            }
            if (quantity > catalogItem.StockQuantity)
            {
                throw new Exceptions.InsufficientStockException(catalogItemId, catalogItem.StockQuantity, quantity);
            }
        }

        // Second pass: take the stock and build the order items.
        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in requested)
        {
            var catalogItem = catalogById[catalogItemId];
            catalogItem.RemoveStock(quantity);
            await _itemRepository.UpdateAsync(catalogItem, cancellationToken);

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }
}
