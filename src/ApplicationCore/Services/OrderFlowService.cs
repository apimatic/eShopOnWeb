using System;
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

public class OrderFlowService : IOrderFlowService
{
    private static readonly Address DefaultAddress = new("123 Main Street", "Seattle", "WA", "USA", "98101");

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly OrderNotificationSender _notificationSender;

    public OrderFlowService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        OrderNotificationSender notificationSender)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _notificationSender = notificationSender;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderAddress? address,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new InvalidRequestException("At least one catalog item is required.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new InvalidRequestException("Each item must include a catalog item id and a quantity greater than zero.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidRequestException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var shipTo = address is null
            ? DefaultAddress
            : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);

        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notificationSender.NotifySafelyAsync(order, OrderNotificationKind.OrderPlaced, sendAt: null, cancellationToken);
        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notificationSender.NotifySafelyAsync(order, OrderNotificationKind.OrderDispatched, sendAt: null, cancellationToken);
        await _notificationSender.NotifySafelyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            DateTimeOffset.UtcNow.Add(OrderNotificationSender.DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notificationSender.CancelPendingFollowUpsSafelyAsync(order, cancellationToken);
        await _notificationSender.NotifySafelyAsync(order, OrderNotificationKind.OrderCancelled, sendAt: null, cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListBuyerNotificationsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notifications.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);
        var toRefresh = notifications.Where(n => !n.IsTerminal).ToList();
        await _notificationSender.RefreshAsync(toRefresh, cancellationToken);
        return notifications;
    }

    public async Task<Order> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        EnsureCallerCanAccess(order, buyerId, isAdministrator);
        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        EnsureCallerCanAccess(order, buyerId, isAdministrator);

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
        await _notificationSender.RefreshAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException("Order was not found.");
        }

        return order;
    }

    private static void EnsureCallerCanAccess(Order order, string buyerId, bool isAdministrator)
    {
        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("This order does not belong to the caller.");
        }
    }
}
