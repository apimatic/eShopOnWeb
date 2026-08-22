using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopOrderService : IShopOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public ShopOrderService(
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

    public async Task<(Order Order, OrderNotification? Notification)> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.", nameof(items));
        }

        var itemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new KeyNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var notification = await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);
        return (order, notification);
    }

    public async Task<(Order Order, IReadOnlyList<OrderNotification> Notifications)> DispatchAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var notifications = await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return (order, notifications);
    }

    public async Task<(Order Order, IReadOnlyList<OrderNotification> Notifications)> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var notifications = await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return (order, notifications);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new CustomerOrdersSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, cancellationToken);
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _orderRepository.GetByIdAsync(orderId, cancellationToken);
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
