using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderRequestItem> items,
        Address shipToAddress,
        CancellationToken ct = default)
    {
        if (items is null || items.Count == 0)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "At least one order item is required." }
            });
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "Every order item must have a quantity greater than zero." }
            });
        }

        // Collapse duplicate catalog ids into a single line, summing quantities.
        var requested = items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var catalogItemsSpecification = new CatalogItemsSpecification(requested.Keys.ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, ct);

        var missing = requested.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<Order>.NotFound(missing.Select(id => $"Catalog item {id} was not found.").ToArray());
        }

        var orderItems = requested.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        return Result<Order>.Success(order);
    }
}
