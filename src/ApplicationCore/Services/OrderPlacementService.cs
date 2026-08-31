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

public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public OrderPlacementService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        var requestedLines = lines?.ToList() ?? new List<OrderLineRequest>();
        Guard.Against.NullOrEmpty(requestedLines, nameof(lines));

        foreach (var line in requestedLines)
        {
            Guard.Against.NegativeOrZero(line.CatalogItemId, nameof(line.CatalogItemId));
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));
        }

        // Sum quantities per catalog id so a repeated item id becomes a single order line.
        var quantityByItemId = requestedLines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItemsSpecification = new CatalogItemsSpecification(quantityByItemId.Keys.ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, cancellationToken);

        var missingIds = quantityByItemId.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Any())
        {
            throw new CatalogItemNotFoundException(missingIds);
        }

        var items = quantityByItemId.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);

        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
