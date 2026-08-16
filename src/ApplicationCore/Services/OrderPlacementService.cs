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
/// Places an order straight from catalog items for the API surface. Prices come from the catalog, so
/// a caller can never dictate what they pay. The order is created awaiting payment.
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    // No storefront address is collected via the API (payment, not shipping, is in scope). We record a
    // placeholder so the existing required ShipToAddress on the Order model is satisfied.
    private static readonly Address PlaceholderAddress =
        new("N/A", "N/A", "N/A", "N/A", "00000");

    public OrderPlacementService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.");

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
        }

        // Collapse duplicate lines for the same catalog item into a single order item.
        var requestedQuantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(requestedQuantities.Keys.ToArray()), cancellationToken);

        var missing = requestedQuantities.Keys.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
            throw new ResourceNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestedQuantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }
}
