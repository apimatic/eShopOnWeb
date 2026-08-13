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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PublicApiOrderService : IPublicApiOrderService
{
    // Shipping is out of scope for the notifications feature; orders placed through the public API
    // carry a placeholder ship-to address so the existing Order model can be reused unchanged.
    private static readonly Address PlaceholderAddress = new("Not provided", "Not provided", "Not provided", "Not provided", "00000");

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;
    private readonly IAppLogger<PublicApiOrderService> _logger;

    public PublicApiOrderService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications,
        IAppLogger<PublicApiOrderService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<OrderOperationResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = items.Select(line =>
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderRequestException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidOrderRequestException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, PlaceholderAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        // The shopper is told their order was placed. Messaging never fails the placement.
        var notifications = await NotifyAndRefreshAsync(order, o => _notifications.NotifyOrderPlacedAsync(o, cancellationToken), cancellationToken);
        return new OrderOperationResult(order, notifications);
    }

    public async Task<OrderOperationResult?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        // The shopper is told it is on its way and a follow-up is queued with the provider.
        var notifications = await NotifyAndRefreshAsync(order, o => _notifications.NotifyOrderDispatchedAsync(o, cancellationToken), cancellationToken);
        return new OrderOperationResult(order, notifications);
    }

    public async Task<OrderOperationResult?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        // The shopper is told, and any queued follow-up is called off before it can go out.
        var notifications = await NotifyAndRefreshAsync(order, o => _notifications.NotifyOrderCancelledAsync(o, cancellationToken), cancellationToken);
        return new OrderOperationResult(order, notifications);
    }

    public async Task<IReadOnlyList<OrderOperationResult>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var results = new List<OrderOperationResult>(orders.Count);
        foreach (var order in orders)
        {
            var notifications = await _notifications.RefreshNotificationsForOrderAsync(order.Id, cancellationToken);
            results.Add(new OrderOperationResult(order, notifications));
        }

        return results;
    }

    /// <summary>
    /// Runs the notification step and returns the order's (refreshed) notifications. A failure anywhere
    /// in messaging is logged and swallowed so it can never fail the order action the caller requested.
    /// </summary>
    private async Task<IReadOnlyList<OrderNotification>> NotifyAndRefreshAsync(Order order, Func<Order, Task> notify, CancellationToken cancellationToken)
    {
        try
        {
            await notify(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {0} action succeeded but its notifications could not be processed: {1}", order.Id, ex.Message);
        }

        try
        {
            return await _notifications.RefreshNotificationsForOrderAsync(order.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not load notifications for order {0}: {1}", order.Id, ex.Message);
            return Array.Empty<OrderNotification>();
        }
    }
}
