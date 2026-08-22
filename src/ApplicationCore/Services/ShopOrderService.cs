using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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
    private readonly OrderNotificationPublisher _publisher;

    public ShopOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderNotificationPublisher publisher)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _publisher = publisher;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new EmptyBasketOnCheckoutException("An order must contain at least one catalog item.");
        }

        foreach (var item in items)
        {
            Guard.Against.NegativeOrZero(item.CatalogItemId, nameof(item.CatalogItemId));
            Guard.Against.NegativeOrZero(item.Quantity, nameof(item.Quantity));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new InvalidOrderStateException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await _publisher.TrySendAsync(order.Id, buyerId, NotificationKinds.OrderPlaced, OrderSmsTemplates.Placed(order.Id), sendAt: null, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _publisher.RefreshAsync(notification, cancellationToken);
        }

        return orders
            .Select(order => new ShopperOrderView(
                order,
                notifications.Where(n => n.OrderId == order.Id).OrderBy(n => n.CreatedAt).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(string buyerId, int orderId, bool isAdministrator, CancellationToken cancellationToken)
    {
        Order? order;
        if (isAdministrator)
        {
            order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        }
        else
        {
            order = await _orders.FirstOrDefaultAsync(new OrderByIdAndBuyerSpec(orderId, buyerId), cancellationToken);
        }

        if (order is null)
        {
            throw new OrderNotFoundException();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _publisher.RefreshAsync(notification, cancellationToken);
        }

        return notifications;
    }
}
