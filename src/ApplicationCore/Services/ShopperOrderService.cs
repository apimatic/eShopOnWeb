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

public class ShopperOrderService : IShopperOrderService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IUriComposer _uriComposer;
    private readonly OrderNotificationSender _notifications;

    public ShopperOrderService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notificationRepository,
        IUriComposer uriComposer,
        OrderNotificationSender notifications)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notificationRepository = notificationRepository;
        _uriComposer = uriComposer;
        _notifications = notifications;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));

        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must include at least one catalog item.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Each order line must have a quantity greater than zero.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new ArgumentException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notifications.TrySendAsync(
            order.Id,
            buyerId,
            NotificationKind.OrderPlaced,
            $"eShopOnWeb: Your order #{order.Id} has been placed. Thank you for shopping with us.",
            sendAt: null,
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _notifications.TrySendAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.OrderDispatched,
            $"eShopOnWeb: Your order #{order.Id} has been dispatched and is on its way.",
            sendAt: null,
            cancellationToken: cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(OrderNotificationSender.DeliveryFollowUpDelay);
        await _notifications.TrySendAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did the delivery of order #{order.Id} go? We would love to hear how it went.",
            sendAt: followUpAt,
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            throw new OrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _notifications.CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await _notifications.TrySendAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.OrderCancelled,
            $"eShopOnWeb: Your order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken: cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(
        string buyerId,
        int orderId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (!isAdministrator && order.BuyerId != buyerId)
        {
            return null;
        }

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderIdSpec(orderId), cancellationToken);
        await _notifications.RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    public Task RefreshNotificationStatusesAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken = default)
    {
        return _notifications.RefreshStatusesAsync(notifications, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderNotification>> ListBuyerNotificationsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerIdSpec(buyerId), cancellationToken);
        await _notifications.RefreshStatusesAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
