using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPlacementService : IOrderPlacementService
{
    private static readonly Address DefaultShipToAddress =
        new("Not provided", "Not provided", "Not provided", "Not provided", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _notificationService;

    public OrderPlacementService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationService = notificationService;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, System.Collections.Generic.IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken ct = default)
    {
        if (items.Count == 0)
        {
            return new PlaceOrderResult(null, "The order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            return new PlaceOrderResult(null, "Item quantities must be positive.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray()), ct);

        var missing = items.Select(i => i.CatalogItemId).Distinct()
            .FirstOrDefault(id => catalogItems.All(c => c.Id != id));
        if (missing != 0)
        {
            return new PlaceOrderResult(null, $"Catalog item {missing} does not exist.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                string.IsNullOrEmpty(catalogItem.PictureUri) ? "n/a" : catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        await _notificationService.NotifyOrderPlacedAsync(order, ct);

        return new PlaceOrderResult(order, null);
    }
}
