using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderLifecycleService : IOrderLifecycleService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public OrderLifecycleService(
        IRepository<Order> orderRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new System.Collections.Generic.KeyNotFoundException("Order was not found.");
        }

        return order;
    }
}
