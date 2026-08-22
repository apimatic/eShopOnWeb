using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFlowService : IOrderFlowService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationDispatcher _dispatcher;
    private readonly IAppLogger<OrderFlowService> _logger;

    public OrderFlowService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderNotification> notifications,
        IUriComposer uriComposer,
        IOrderNotificationDispatcher dispatcher,
        IAppLogger<OrderFlowService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ArgumentException("At least one catalog item is required.");
        }

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new KeyNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = request.ShipToAddress ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, orderItems);
        await _orders.AddAsync(order, cancellationToken);

        await TryNotifyAsync(
            order.Id,
            buyerId,
            NotificationKind.OrderPlaced,
            $"Your eShopOnWeb order #{order.Id} has been placed. Thank you!",
            sendAt: null,
            cancellationToken);

        return order;
    }

    public async Task<ShopperOrdersResult> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        await _dispatcher.RefreshFromProviderAsync(notifications, cancellationToken);
        return new ShopperOrdersResult(orders, notifications);
    }

    public async Task DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await TryNotifyAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        await TryNotifyAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        try
        {
            await _dispatcher.CancelScheduledFollowUpsAsync(order.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to cancel scheduled follow-ups for order {OrderId}: {Message}", order.Id, ex.Message);
        }

        await TryNotifyAsync(
            order.Id,
            order.BuyerId,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);
    }

    public async Task<Order?> GetOrderForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpecification(orderId), cancellationToken);
        if (order == null)
        {
            return null;
        }

        if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        return order;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderForCallerAsync(orderId, buyerId, isAdministrator, cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException("Order was not found.");
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderIdSpecification(orderId), cancellationToken);
        await _dispatcher.RefreshFromProviderAsync(notifications, cancellationToken);
        return notifications;
    }

    private async Task TryNotifyAsync(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.NotifyAsync(orderId, buyerId, kind, body, sendAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification {Kind} for order {OrderId} failed: {Message}", kind, orderId, ex.Message);
        }
    }
}
