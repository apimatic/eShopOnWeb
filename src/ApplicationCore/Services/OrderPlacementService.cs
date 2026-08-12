using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places an order for a shopper from catalog item ids and quantities, reusing the app's existing
/// Order/OrderItem model, then tells the shopper their order was placed. Messaging is best-effort and
/// never fails the placement.
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    // Orders placed through the notification API have no shipping-address step; the capability is about
    // messaging, so a placeholder address keeps the existing (required) Order model intact.
    private static readonly Address PlaceholderAddress = new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<Order> _orders;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public OrderPlacementService(
        IRepository<CatalogItem> catalogItems,
        IRepository<Order> orders,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _catalogItems = catalogItems;
        _orders = orders;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return new OrderPlacementResult(false, 0, "An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity < 1))
        {
            return new OrderPlacementResult(false, 0, "Every item quantity must be at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            return new OrderPlacementResult(false, 0, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress, items);
        await _orders.AddAsync(order, cancellationToken);

        // Best-effort; never throws, never fails the placement.
        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        return new OrderPlacementResult(true, order.Id, null);
    }
}
