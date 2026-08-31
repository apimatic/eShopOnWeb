using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the shopper-facing SMS notifications as an order moves, and records what became
/// of each message. Provider failures are recorded, never thrown: a message that cannot
/// be sent must not fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order was cancelled and calls off any follow-up that has not gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. A repeated idempotency key returns
    /// the notification the first attempt produced without sending again. Returns null when
    /// no notification with the given id exists.
    /// </summary>
    Task<ResendNotificationResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refreshes a notification's delivery outcome from the provider (best-effort).</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default);
}

public sealed record ResendNotificationResult(OrderNotification Notification, bool AlreadyExisted);
