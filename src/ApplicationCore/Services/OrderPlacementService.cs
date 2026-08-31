using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<OperationResult<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return OperationResult<Order>.Invalid("At least one order item is required.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return OperationResult<Order>.Invalid("Every order item must have a quantity of at least 1.");
        }

        // Merge duplicate catalog item ids so a caller can send the same item twice without surprises.
        var mergedQuantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(mergedQuantities.Keys.ToArray()), cancellationToken);

        var missing = mergedQuantities.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return OperationResult<Order>.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = mergedQuantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        return OperationResult<Order>.Ok(order);
    }
}
