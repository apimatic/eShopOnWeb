using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    // No storefront collects a shipping address for the API-placed order, so a placeholder is used —
    // the same approach the existing Web checkout takes.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

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

    public async Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLine> lines)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var requestedLines = lines?.ToList() ?? new List<OrderLine>();
        Guard.Against.NullOrEmpty(requestedLines, nameof(lines));
        foreach (var line in requestedLines)
        {
            Guard.Against.OutOfRange(line.CatalogItemId, nameof(line.CatalogItemId), 1, int.MaxValue);
            Guard.Against.NegativeOrZero(line.Quantity, nameof(line.Quantity));
        }

        var catalogItemIds = requestedLines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var orderItems = requestedLines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            Guard.Against.Null(catalogItem, nameof(catalogItem),
                $"No catalog item found with id {line.CatalogItemId}.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order);
        return order;
    }
}
