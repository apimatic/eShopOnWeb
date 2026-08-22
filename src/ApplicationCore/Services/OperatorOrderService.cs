using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OperatorOrderService : IOperatorOrderService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly OrderSmsNotifier _notifier;

    public OperatorOrderService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        OrderSmsNotifier notifier)
    {
        _orders = orders;
        _notifications = notifications;
        _notifier = notifier;
    }

    public async Task<ShopperOrderDetails> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException();
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);
        await _notifier.NotifyOrderDispatchedAsync(order, cancellationToken);

        var all = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(order.Id), cancellationToken);
        return new ShopperOrderDetails(order, all);
    }

    public async Task<ShopperOrderDetails> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException();
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        await _notifier.NotifyOrderCancelledAsync(order, cancellationToken);

        var all = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(order.Id), cancellationToken);
        return new ShopperOrderDetails(order, all);
    }
}
