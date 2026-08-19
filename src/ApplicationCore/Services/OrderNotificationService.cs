using System;
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

/// <summary>
/// Places orders from catalog items (reusing the existing Order aggregate) and drives the
/// notifications that go out as an order is placed, dispatched or cancelled. A notification
/// that cannot be sent never fails the underlying order operation.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    // Orders placed through the notification API have no address collection step, so a
    // placeholder ship-to address satisfies the existing (required) Order model.
    private static readonly Address UnspecifiedAddress = new("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<Notification> _notifications;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<Notification> notifications,
        INotificationDispatcher dispatcher,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _dispatcher = dispatcher;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new InvalidRequestException("An order must contain at least one item.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
                throw new InvalidRequestException("Each order line must have a quantity of at least 1.");
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new InvalidRequestException($"Catalog item {line.CatalogItemId} was not found.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
                pictureUri = "eCatalog-item-default.png";

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, UnspecifiedAddress, items);
        order = await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for a shopper with {LineCount} line(s).", order.Id, items.Count);

        await _dispatcher.SendOrderEventAsync(order, NotificationKind.OrderPlaced, cancellationToken);
        return order;
    }

    public async Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} marked dispatched.", orderId);

        await _dispatcher.SendOrderEventAsync(order, NotificationKind.OrderDispatched, cancellationToken);
        await _dispatcher.ScheduleDeliveryFollowUpAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
            return null;

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} marked cancelled.", orderId);

        // Call off any not-yet-sent follow-up first, so a cancelled order never asks how delivery went.
        await _dispatcher.CancelScheduledFollowUpsAsync(orderId, cancellationToken);
        await _dispatcher.SendOrderEventAsync(order, NotificationKind.OrderCancelled, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderWithNotifications>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(order.Id), cancellationToken);
            await _dispatcher.RefreshStatusesAsync(notifications, cancellationToken);
            result.Add(new OrderWithNotifications(order, notifications));
        }

        return result;
    }

    public async Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Scoped to the caller: one shopper never sees another's order notifications.
        if (order is null || order.BuyerId != buyerId)
            return null;

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await _dispatcher.RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }
}
