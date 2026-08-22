using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ShopOrderService : IShopOrderService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly OrderSmsNotifier _notifier;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderSmsNotifier notifier)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _notifier = notifier;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        ShippingAddressRequest? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new CatalogOrderException("An order must contain at least one item.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new CatalogOrderException("Each item quantity must be greater than zero.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new CatalogOrderException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(requestItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requestItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestItem.Quantity);
        }).ToList();

        var address = shipTo == null
            ? DefaultShipTo
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await _notifier.NotifyOrderPlacedAsync(order, cancellationToken);

        return new PlaceOrderResult(order.Id, order.Status);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException();

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notifier.NotifyOrderDispatchedAsync(order, cancellationToken);
        await _notifier.QueueDeliveryFollowUpAsync(order, cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException();

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notifier.CancelQueuedFollowUpsAsync(order, cancellationToken);
        await _notifier.NotifyOrderCancelledAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = orderIds.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(orderIds), cancellationToken);

        await _notifier.RefreshStatusesAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return orders.Select(order =>
        {
            byOrder.TryGetValue(order.Id, out var orderNotifications);
            return ToOrderView(order, orderNotifications ?? new List<OrderNotification>());
        }).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await _notifier.RefreshStatusesAsync(notifications, cancellationToken);
        return notifications.Select(ToNotificationView).ToList();
    }

    internal static OrderView ToOrderView(Order order, IReadOnlyList<OrderNotification> notifications) =>
        new(
            order.Id,
            order.Status,
            order.OrderDate,
            order.Total(),
            order.OrderItems.Select(i => new OrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
            notifications.Select(ToNotificationView).ToList());

    internal static NotificationView ToNotificationView(OrderNotification notification) =>
        new(
            notification.Id,
            notification.Kind,
            notification.ProviderMessageSid,
            notification.ProviderStatus,
            notification.ContentRedacted ? string.Empty : notification.Body,
            notification.ProviderErrorCode,
            notification.CreatedAt,
            notification.ScheduledFor,
            notification.ProviderDateSent,
            notification.ContentRedacted);
}
