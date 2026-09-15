using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orders;
    private readonly OrderSmsDispatcher _smsDispatcher;

    public OrderFulfillmentService(IRepository<Order> orders, OrderSmsDispatcher smsDispatcher)
    {
        _orders = orders;
        _smsDispatcher = smsDispatcher;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _smsDispatcher.NotifyOrderAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Good news — your eShopOnWeb order #{order.Id} is on its way.",
            sendAt: null,
            cancellationToken);

        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await _smsDispatcher.NotifyOrderAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did the delivery of eShopOnWeb order #{order.Id} go? We'd love your feedback.",
            sendAt: followUpAt,
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await _smsDispatcher.CancelPendingFollowUpsAsync(order.Id, cancellationToken);

        await _smsDispatcher.NotifyOrderAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShopOnWeb order #{order.Id} has been cancelled.",
            sendAt: null,
            cancellationToken);

        return order;
    }
}
