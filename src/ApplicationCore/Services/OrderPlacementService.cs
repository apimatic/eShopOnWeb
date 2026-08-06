using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
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

    public OrderPlacementService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (lines is null || lines.Count == 0)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new ValidationError { ErrorMessage = "An order must contain at least one item." }
            });
        }

        if (lines.Any(l => l.Quantity < 1))
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new ValidationError { ErrorMessage = "Item quantities must be at least 1." }
            });
        }

        // Collapse duplicate lines for the same catalog item into a single line.
        var quantitiesByItem = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var ids = quantitiesByItem.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<Order>.NotFound($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = quantitiesByItem.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price is taken from the catalog at placement time; currency is USD (Order default).
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        return Result<Order>.Success(order);
    }
}
