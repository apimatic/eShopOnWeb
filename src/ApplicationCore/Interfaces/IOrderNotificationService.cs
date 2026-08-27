using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates shopper notifications as orders move. Messaging failures never
/// propagate: the underlying order operation always succeeds.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order was cancelled and cancels any not-yet-sent follow-up at the provider.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key guarantees
    /// a repeated request under the same key does not send a second message.
    /// Returns the notification the resend produced (new, or the replayed earlier one).
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text both locally and at the provider.</summary>
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Pulls the current delivery outcome for a message from the provider.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}
