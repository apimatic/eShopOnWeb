using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PlaceOrderService : IPlaceOrderService
{
    private static readonly Address DefaultShipToAddress =
        new("1234 Main St.", "Kent", "WA", "United States", "98031");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public PlaceOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Result<Order>> PlaceAsync(string buyerId, IReadOnlyList<OrderLineRequest> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (items is null || items.Count == 0)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "At least one catalog item is required." }
            });
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "Each item must include a catalog item id and a quantity greater than zero." }
            });
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
        if (catalogItems.Count != catalogItemIds.Length)
        {
            return Result<Order>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "items", ErrorMessage = "One or more catalog items were not found." }
            });
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order);
        return Result<Order>.Success(order);
    }
}
