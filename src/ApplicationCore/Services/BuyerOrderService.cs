using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class BuyerOrderService : IBuyerOrderService
{
    private static readonly Address DefaultShippingAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationSender _notificationSender;

    public BuyerOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        IOrderNotificationSender notificationSender)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _notificationSender = notificationSender;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException();
        }

        var requested = items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineRequest(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (requested.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException();
        }

        var catalogItems = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(requested.Select(i => i.CatalogItemId).ToArray()),
            cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem == null)
            {
                throw new CatalogItemNotFoundException(line.CatalogItemId);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                    ? "placeholder"
                    : _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShippingAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notificationSender.TryNotifyAsync(
            order,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Total: {order.Total():0.00}.",
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException();
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _notificationSender.SyncFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }
}
