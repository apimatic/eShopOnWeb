using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    private readonly IRepository<Order> _orders;
    private readonly IMessagingProvider _messaging;
    private readonly OrderNotificationPublisher _publisher;

    public OrderFulfillmentService(
        IRepository<Order> orders,
        IMessagingProvider messaging,
        OrderNotificationPublisher publisher)
    {
        _orders = orders;
        _messaging = messaging;
        _publisher = publisher;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException();
        }

        if (order.Status == OrderFulfillmentStatus.Dispatched)
        {
            throw new InvalidOrderStateException("The order has already been dispatched.");
        }

        try
        {
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOrderStateException(ex.Message);
        }

        await _orders.UpdateAsync(order, cancellationToken);

        await _publisher.TrySendAsync(
            order.Id,
            order.BuyerId,
            NotificationKinds.OrderDispatched,
            OrderSmsTemplates.Dispatched(order.Id),
            sendAt: null,
            cancellationToken);

        var sendAt = DateTimeOffset.UtcNow.Add(_messaging.FollowUpDelay);
        await _publisher.TrySendAsync(
            order.Id,
            order.BuyerId,
            NotificationKinds.DeliveryFollowUp,
            OrderSmsTemplates.DeliveryFollowUp(order.Id),
            sendAt,
            cancellationToken);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException();
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        await _publisher.TryCancelPendingFollowUpAsync(order.Id, cancellationToken);

        await _publisher.TrySendAsync(
            order.Id,
            order.BuyerId,
            NotificationKinds.OrderCancelled,
            OrderSmsTemplates.Cancelled(order.Id),
            sendAt: null,
            cancellationToken);

        return order;
    }
}
