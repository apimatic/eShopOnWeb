using System;
using System.Collections.Generic;
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
    private readonly IAppLogger<OrderLifecycleService> _logger;

    public OrderLifecycleService(
        IRepository<Order> orderRepository,
        IOrderNotificationService notificationService,
        IAppLogger<OrderLifecycleService> logger)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} was dispatched but notification failed: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Order {OrderId} was cancelled but notification failed: {Message}", order.Id, ex.Message);
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }
}
