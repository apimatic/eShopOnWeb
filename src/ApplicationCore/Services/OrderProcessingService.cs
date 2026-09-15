using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderProcessingService : IOrderProcessingService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;

    public OrderProcessingService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notifications)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notifications = notifications;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (lines is null || lines.Count == 0)
        {
            return PlaceOrderResult.Rejected("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return PlaceOrderResult.Rejected("Every item quantity must be greater than zero.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return PlaceOrderResult.Rejected($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        // Tell the shopper their order was placed. A messaging failure never fails the order.
        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);

        return PlaceOrderResult.Ok(order);
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
            order.MarkDispatched();
        }
        catch (InvalidOrderStateException ex)
        {
            return OrderOperationResult.Invalid(order, ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Tell the shopper it is on its way and queue the delivery follow-up for a few days later.
        await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);

        return OrderOperationResult.Ok(order);
    }

    public async Task<OrderOperationResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OrderOperationResult.NotFound();
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOrderStateException ex)
        {
            return OrderOperationResult.Invalid(order, ex.Message);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);

        // Tell the shopper it was cancelled and call off any delivery follow-up not yet sent.
        await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);

        return OrderOperationResult.Ok(order);
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return new List<OrderWithNotifications>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = await _notifications.GetNotificationsForOrdersAsync(orderIds, cancellationToken);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .Select(o => new OrderWithNotifications(
                o,
                byOrder.TryGetValue(o.Id, out var list) ? list : new List<OrderNotification>()))
            .ToList();
    }

    public async Task<OrderNotificationsView> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OrderNotificationsView.NotFound();
        }

        // A shopper must never see another's order.
        if (order.BuyerId != buyerId)
        {
            return OrderNotificationsView.NotOwned();
        }

        var notifications = await _notifications.GetOrderNotificationsAsync(orderId, cancellationToken);
        return OrderNotificationsView.Owned(notifications);
    }
}
