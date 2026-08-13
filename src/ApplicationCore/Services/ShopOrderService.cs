using System;
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
/// Coordinates an order's lifecycle with the shopper notifications that accompany it. The order state
/// change is always persisted before its notification is attempted, and the notification is best-effort:
/// it can never turn a successful placement, dispatch or cancellation into a failure.
/// </summary>
public class ShopOrderService : IShopOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<ShopOrderService> _logger;

    public ShopOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notifications,
        IUriComposer uriComposer,
        IAppLogger<ShopOrderService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return new PlaceOrderResult { Error = "An order must contain at least one item." };
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return new PlaceOrderResult { Error = "Every item quantity must be greater than zero." };
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return new PlaceOrderResult { Error = $"Unknown catalog item id(s): {string.Join(", ", missing)}." };
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        await SafelyNotifyAsync(() => _notifications.NotifyOrderPlacedAsync(order, cancellationToken),
            "order placed", order.Id);

        return new PlaceOrderResult { Order = order };
    }

    public async Task<OrderOperationResult> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OrderOperationResult.NotFound();
        }

        try
        {
            order.Dispatch();
        }
        catch (InvalidOperationException ex)
        {
            return OrderOperationResult.InvalidState(ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        await SafelyNotifyAsync(() => _notifications.NotifyOrderDispatchedAsync(order, cancellationToken),
            "order dispatched", order.Id);

        return OrderOperationResult.Ok(order);
    }

    public async Task<OrderOperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OrderOperationResult.NotFound();
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await SafelyNotifyAsync(() => _notifications.NotifyOrderCancelledAsync(order, cancellationToken),
            "order cancelled", order.Id);

        return OrderOperationResult.Ok(order);
    }

    private async Task SafelyNotifyAsync(Func<Task> notify, string what, int orderId)
    {
        try
        {
            await notify();
        }
        catch (Exception ex)
        {
            // Messaging is best-effort: never let it fail the order operation that already succeeded.
            _logger.LogWarning("Notification for {What} (order {OrderId}) could not be completed: {Error}",
                what, orderId, ex.Message);
        }
    }
}
