using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PlaceOrderService : IPlaceOrderService
{
    private static readonly Address DefaultShipTo = new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;

    public PlaceOrderService(
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new ArgumentException("Each line must include a catalog item id and a positive quantity.");
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => (Item: catalogItems.First(c => c.Id == g.Key), Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        var orderItems = grouped.Select(line =>
        {
            var itemOrdered = new CatalogItemOrdered(
                line.Item.Id,
                line.Item.Name,
                _uriComposer.ComposePicUri(line.Item.PictureUri));
            return new OrderItem(itemOrdered, line.Item.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }
}
