using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderService : IOperatorOrderService
{
    private readonly IRepository<Order> _orders;
    private readonly OrderNotificationSender _notificationSender;

    public OperatorOrderService(IRepository<Order> orders, OrderNotificationSender notificationSender)
    {
        _orders = orders;
        _notificationSender = notificationSender;
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
            throw new OrderNotificationException(409, ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);
        await _notificationSender.NotifyDispatchedAsync(order.Id, order.BuyerId, cancellationToken);
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
            throw new OrderNotificationException(409, ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);
        await _notificationSender.NotifyCancelledAsync(order.Id, order.BuyerId, cancellationToken);
        return order;
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotificationException(404, "Order not found.");
        }

        return order;
    }
}
