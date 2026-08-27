using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends SMS notifications as orders move and records what became of each message.
/// Notification failures never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order was cancelled and calls off any follow-up that has not gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of an existing notification. Repeating under the same idempotency
    /// key returns the notification the first attempt produced without sending again.
    /// Returns null when the source notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text both at the provider and locally, keeping the fact that
    /// a message was sent and its outcome. Returns false when the notification does not exist.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Best-effort refresh of a notification's delivery outcome from the provider.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}
