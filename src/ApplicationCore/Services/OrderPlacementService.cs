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

/// <summary>
/// Places an order from catalog item ids and quantities. Reuses the existing <see cref="Order"/>,
/// <see cref="OrderItem"/> and <see cref="CatalogItemOrdered"/> model — the same aggregate basket
/// checkout produces — rather than a parallel one.
/// </summary>
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

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            throw new InvalidOrderRequestException("An order must contain at least one item.");

        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidOrderRequestException("Every order line must have a quantity of at least one.");

        // Combine repeated ids into a single line so the same catalog item is not ordered twice.
        var quantitiesByItem = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var ids = quantitiesByItem.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantitiesByItem[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }
}
