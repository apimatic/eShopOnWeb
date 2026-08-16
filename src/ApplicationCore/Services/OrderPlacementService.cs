using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places an order straight from catalog items (used by the API, which has no basket).
/// Mirrors the basket-based <see cref="OrderService"/> but takes catalog ids + quantities.
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

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.", PaymentErrorReason.Validation);
        }

        // Collapse duplicate lines for the same catalog item.
        var quantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", PaymentErrorReason.Validation);
            }
            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var catalogItemIds = quantities.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var missing = catalogItemIds.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", PaymentErrorReason.Validation);
        }

        var items = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        // The API has no shipping-address step; use a placeholder address as the sample checkout does.
        var order = new Order(buyerId, new Address("N/A", "N/A", "N/A", "N/A", "00000"), items);
        return await _orderRepository.AddAsync(order);
    }
}
