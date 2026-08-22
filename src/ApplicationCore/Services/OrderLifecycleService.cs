using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderLifecycleService : IOrderLifecycleService
{
    public static readonly TimeSpan DeliveryFollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public OrderLifecycleService(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderFulfillmentStatus.Dispatched)
        {
            return order;
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notifications.TryNotifyAsync(order, OrderNotificationKind.OrderDispatched, sendAt: null, cancellationToken);
        await _notifications.TryNotifyAsync(
            order,
            OrderNotificationKind.DeliveryFollowUp,
            sendAt: DateTimeOffset.UtcNow.Add(DeliveryFollowUpDelay),
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        var alreadyCancelled = order.Status == OrderFulfillmentStatus.Cancelled;
        if (!alreadyCancelled)
        {
            order.MarkCancelled();
            await _orders.UpdateAsync(order, cancellationToken);
        }

        await _notifications.CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        if (!alreadyCancelled)
        {
            await _notifications.TryNotifyAsync(order, OrderNotificationKind.OrderCancelled, sendAt: null, cancellationToken);
        }

        return order;
    }
}
