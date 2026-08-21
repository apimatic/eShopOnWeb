using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class ShopperOrderService : IShopperOrderService
{
    private static readonly Address DefaultShipToAddress = new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public ShopperOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogQuantity> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Each item quantity must be greater than zero.", nameof(items));
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.", nameof(items));
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        return order;
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order not found.");
        }

        return order;
    }
}
