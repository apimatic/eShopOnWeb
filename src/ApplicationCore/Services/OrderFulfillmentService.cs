using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    public const int FollowUpDelayHours = 72;

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsNotificationGateway _gateway;
    private readonly OrderNotificationPublisher _publisher;
    private readonly IAppLogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        ISmsNotificationGateway gateway,
        OrderNotificationPublisher publisher,
        IAppLogger<OrderFulfillmentService> logger)
    {
        _orders = orders;
        _notifications = notifications;
        _gateway = gateway;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<OrderFulfillmentResult> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        var notifications = new List<OrderNotification>();

        var dispatched = await _publisher.PublishAsync(
            order,
            NotificationKinds.OrderDispatched,
            $"Your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            sourceNotificationId: null,
            cancellationToken);
        if (dispatched != null)
        {
            notifications.Add(dispatched);
        }

        var followUp = await _publisher.PublishAsync(
            order,
            NotificationKinds.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go?",
            sendAt: DateTimeOffset.UtcNow.AddHours(FollowUpDelayHours),
            sourceNotificationId: null,
            cancellationToken);
        if (followUp != null)
        {
            notifications.Add(followUp);
        }

        return new OrderFulfillmentResult(order, notifications);
    }

    public async Task<OrderFulfillmentResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        var notifications = new List<OrderNotification>();

        await CancelPendingFollowUpAsync(order, cancellationToken);

        var cancelled = await _publisher.PublishAsync(
            order,
            NotificationKinds.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            sourceNotificationId: null,
            cancellationToken);
        if (cancelled != null)
        {
            notifications.Add(cancelled);
        }

        return new OrderFulfillmentResult(order, notifications);
    }

    public async Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToArray();
        var notifications = orderIds.Length == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpec(orderIds), cancellationToken);

        foreach (var notification in notifications)
        {
            await _publisher.RefreshProviderStateAsync(notification, cancellationToken);
        }

        return orders
            .OrderByDescending(o => o.Id)
            .Select(order => new ShopperOrderView(
                order,
                notifications.Where(n => n.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(
        int orderId,
        string? buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!isAdministrator && (buyerId is null || order.BuyerId != buyerId))
        {
            throw new OrderNotFoundException(orderId);
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _publisher.RefreshProviderStateAsync(notification, cancellationToken);
        }

        return notifications;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task CancelPendingFollowUpAsync(Order order, CancellationToken cancellationToken)
    {
        var followUps = await _notifications.ListAsync(new DeliveryFollowUpByOrderSpec(order.Id), cancellationToken);
        foreach (var followUp in followUps)
        {
            if (string.IsNullOrEmpty(followUp.ProviderSid))
            {
                continue;
            }

            if (IsTerminalStatus(followUp.ProviderStatus))
            {
                continue;
            }

            try
            {
                var result = await _gateway.CancelScheduledAsync(followUp.ProviderSid, cancellationToken);
                followUp.ApplyProviderState(
                    result.ProviderSid ?? followUp.ProviderSid,
                    result.Status ?? followUp.ProviderStatus,
                    result.ErrorCode,
                    result.ErrorMessage);
                await _notifications.UpdateAsync(followUp, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Failed to cancel follow-up notification {NotificationId}: {Error}", followUp.Id, ex.GetType().Name);
                await _publisher.RefreshProviderStateAsync(followUp, cancellationToken);
            }
        }
    }

    private static bool IsTerminalStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return false;
        }

        return status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("delivered", StringComparison.OrdinalIgnoreCase)
            || status.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
            || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("sent", StringComparison.OrdinalIgnoreCase)
            || status.Equals("received", StringComparison.OrdinalIgnoreCase);
    }
}
