using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public sealed class OperatorOrderService : IOperatorOrderService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IOrderNotificationService _notifier;

    public OperatorOrderService(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IOrderNotificationService notifier)
    {
        _orders = orders;
        _notifications = notifications;
        _notifier = notifier;
    }

    public async Task<OperatorTransitionResult> DispatchAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order == null)
        {
            return OperatorTransitionResult.NotFound;
        }

        // Gate the notification + follow-up on a real transition — repeating a dispatch sends nothing.
        if (!order.MarkDispatched())
        {
            return OperatorTransitionResult.NoOp;
        }

        await _orders.UpdateAsync(order, ct);          // persist the transition before notifying
        await _notifier.SendDispatchedAsync(order, ct);
        return OperatorTransitionResult.Done;
    }

    public async Task<OperatorTransitionResult> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order == null)
        {
            return OperatorTransitionResult.NotFound;
        }

        if (!order.MarkCancelled())
        {
            return OperatorTransitionResult.NoOp;
        }

        await _orders.UpdateAsync(order, ct);
        await _notifier.SendCancelledAsync(order, ct);
        return OperatorTransitionResult.Done;
    }

    public async Task<ResendActionResult> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken ct)
    {
        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original == null)
        {
            return ResendActionResult.Missing();
        }

        var outcome = await _notifier.ResendAsync(original, idempotencyKey, ct);
        return new ResendActionResult(false, outcome.NotificationId, outcome.WasDuplicate);
    }

    public async Task<bool> RedactContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification == null)
        {
            return false;
        }

        // May throw the provider exception — content disposal is an operator action, so a failure surfaces.
        await _notifier.RedactContentAsync(notification, ct);
        return true;
    }

    public Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct) =>
        _notifier.ReconcileAsync(from, to, ct);
}
