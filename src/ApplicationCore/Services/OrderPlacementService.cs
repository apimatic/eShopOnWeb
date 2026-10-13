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

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new EmptyOrderException();
        }

        // Collapse duplicate lines for the same catalog item so stock is checked against the total.
        var requestedByItem = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        foreach (var (catalogItemId, quantity) in requestedByItem)
        {
            Guard.Against.OutOfRange(catalogItemId, nameof(catalogItemId), 1, int.MaxValue);
            Guard.Against.NegativeOrZero(quantity, nameof(quantity));
        }

        var itemIds = requestedByItem.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(ci => ci.Id);

        var missing = itemIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new CatalogItemNotFoundException(missing);
        }

        // Validate all lines against available stock before mutating anything.
        foreach (var (catalogItemId, quantity) in requestedByItem)
        {
            var available = catalogById[catalogItemId].StockQuantity;
            if (quantity > available)
            {
                throw new InsufficientStockException(catalogItemId, available, quantity);
            }
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Deduct eShop stock for each catalog item, then persist the updates.
        foreach (var (catalogItemId, quantity) in requestedByItem)
        {
            catalogById[catalogItemId].RemoveStock(quantity);
        }
        await _itemRepository.UpdateRangeAsync(catalogItems, cancellationToken);

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        return order.Id;
    }
}
