using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Seattle", "WA", "United States", "98101");

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

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogItemQuantity> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ArgumentException("A signed-in shopper is required.", nameof(buyerId));
        }

        if (items is null || items.Count == 0)
        {
            throw new OrderFlowException("At least one catalog item is required.");
        }

        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new OrderFlowException("Each line must include a catalog item id and a quantity greater than zero.");
            }
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CatalogItemQuantity(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(grouped.Select(i => i.CatalogItemId).ToArray()),
            cancellationToken);

        var missing = grouped
            .Select(i => i.CatalogItemId)
            .Except(catalogItems.Select(c => c.Id))
            .ToList();
        if (missing.Count > 0)
        {
            throw new OrderFlowException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
