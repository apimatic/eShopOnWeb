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

/// <summary>
/// Places an order straight from catalog items and quantities, reusing the existing Order/OrderItem
/// model. This mirrors what <see cref="OrderService"/> does when turning a basket into an order, but
/// takes catalog ids directly so the flow is drivable through the API without a basket. Prices are
/// always taken from the catalog, never from the caller.
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    // eShopOnWeb has no shipping-address capture on this billing surface; it uses the same placeholder
    // address the storefront checkout uses, since the invoicing feature bills rather than ships.
    private static readonly Address PlaceholderAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var requestedLines = (lines ?? Enumerable.Empty<OrderLineRequest>())
            .Where(line => line is not null)
            .ToList();

        if (requestedLines.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        if (requestedLines.Any(line => line.Quantity <= 0))
        {
            throw new InvalidOrderRequestException("Every ordered item must have a quantity of at least one.");
        }

        var catalogItemIds = requestedLines.Select(line => line.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in requestedLines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new InvalidOrderRequestException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));

            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, PlaceholderAddress, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
