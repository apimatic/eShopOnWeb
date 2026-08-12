using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Operator actions that move an order along and tell the shopper. The order state change is committed
/// first; the accompanying messaging is best-effort and never fails the operation.
/// </summary>
public class OrderFulfillmentService : IOrderFulfillmentService
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notificationService;

    public OrderFulfillmentService(IRepository<Order> orders, IOrderNotificationService notificationService)
    {
        _orders = orders;
        _notificationService = notificationService;
    }

    public async Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return new OrderActionResult(ActionOutcome.NotFound, "Order not found.");
        }

        if (order.Status == OrderStatus.Dispatched)
        {
            return new OrderActionResult(ActionOutcome.Invalid, "Order has already been dispatched.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return new OrderActionResult(ActionOutcome.Invalid, "A cancelled order cannot be dispatched.");
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

        return new OrderActionResult(ActionOutcome.Success, null);
    }

    public async Task<OrderActionResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return new OrderActionResult(ActionOutcome.NotFound, "Order not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return new OrderActionResult(ActionOutcome.Invalid, "Order has already been cancelled.");
        }

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

        return new OrderActionResult(ActionOutcome.Success, null);
    }
}
