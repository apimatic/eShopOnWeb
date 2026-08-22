using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new System.ArgumentException("At least one catalog item is required.", nameof(items));
        }

        var requested = items
            .Where(i => i.CatalogItemId > 0 && i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CatalogQuantity(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (requested.Count == 0)
        {
            throw new System.ArgumentException("At least one catalog item with a positive quantity is required.", nameof(items));
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(requested.Select(i => i.CatalogItemId).ToArray()),
            cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new KeyNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "placeholder"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
