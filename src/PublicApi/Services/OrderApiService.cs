using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IOrderApiService
{
    /// <summary>
    /// Places an order for the buyer from catalog item ids and quantities, reusing the
    /// existing order/order-item model, then notifies the buyer. Returns null when any
    /// catalog item id is unknown.
    /// </summary>
    Task<Order?> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address? shipToAddress, CancellationToken ct);

    /// <summary>Marks an order dispatched and notifies the buyer. Returns null when not found.</summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken ct);

    /// <summary>Cancels an order, notifies the buyer, and calls off any pending follow-up. Returns null when not found.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>The buyer's orders with their notifications, delivery states refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> ListMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>The notifications for one of the buyer's own orders; null when not found or not owned.</summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);
}

public sealed record OrderItemRequest(int CatalogItemId, int Units);

public sealed record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public class OrderApiService : IOrderApiService
{
    private static readonly Address DefaultShipToAddress = new("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public OrderApiService(
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public async Task<Order?> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address? shipToAddress, CancellationToken ct)
    {
        var distinctIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(distinctIds), ct);

        if (catalogItems.Count != distinctIds.Length)
        {
            return null;
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Units);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        // Notification failures never fail the order.
        await _notificationService.NotifyOrderPlacedAsync(order, ct);
        return order;
    }

    public async Task<Order?> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, ct);

        // Notification failures never fail the dispatch.
        await _notificationService.NotifyOrderDispatchedAsync(order, ct);
        return order;
    }

    public async Task<Order?> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        // Notification failures never fail the cancellation.
        await _notificationService.NotifyOrderCancelledAsync(order, ct);
        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var notifications = await _notificationRepository.ListAsync(new NotificationsForBuyerSpecification(buyerId), ct);

        // No webhooks exist: ask the provider for the current state of anything not yet settled.
        await _notificationService.RefreshDeliveryStatesAsync(notifications, ct);

        var notificationsByOrder = notifications.GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderNotification>)g.ToList());

        return orders
            .Select(o => new OrderWithNotifications(o, notificationsByOrder.GetValueOrDefault(o.Id, System.Array.Empty<OrderNotification>())))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);

        // Another shopper's order is indistinguishable from a missing one.
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsForOrderSpecification(orderId), ct);
        await _notificationService.RefreshDeliveryStatesAsync(notifications, ct);
        return notifications;
    }
}
