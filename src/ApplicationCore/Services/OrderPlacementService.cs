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

/// <summary>
/// Places an order directly from catalog items, snapshotting item details into the existing
/// order/order-item model exactly as the storefront checkout does.
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

    public async Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return OrderPlacementResult.Fail("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return OrderPlacementResult.Fail("Every order item must have a quantity of at least one.");
        }

        // Consolidate duplicate catalog item ids into a single line each.
        var requestedQuantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var itemsSpec = new CatalogItemsSpecification(requestedQuantities.Keys.ToArray());
        var catalogItems = await _itemRepository.ListAsync(itemsSpec, cancellationToken);

        var missing = requestedQuantities.Keys
            .Where(id => catalogItems.All(c => c.Id != id))
            .ToList();
        if (missing.Count > 0)
        {
            return OrderPlacementResult.Fail($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = requestedQuantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        // The order model requires a ship-to address; API-placed orders are billing-only, so a
        // placeholder address is used. Billing customer details are captured on the invoice instead.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        return OrderPlacementResult.Ok(order.Id);
    }
}
