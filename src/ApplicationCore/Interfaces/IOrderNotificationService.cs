using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the messages that go out as an order moves, records what was sent and what became of
/// each message, and supports the operator actions over those records. No method here ever lets a
/// messaging failure propagate out to fail the order action that triggered it.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Persists a notification per registered number.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way, and queues a "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled, and calls off any queued follow-up that has not yet
    /// gone out so it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper (operator action). Repeating the request under
    /// the same <paramref name="idempotencyKey"/> does not send a second message; a fresh key does.
    /// Returns the notification the resend produced (existing one on a repeat).
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content (operator action) so its text is no longer retrievable from the
    /// provider either, while the fact it was sent and what became of it survive. Returns false if the
    /// notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the notifications for an order (scoped to the given owner), each refreshed to the
    /// provider's current delivery outcome. Returns null if the order does not belong to the owner.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes and returns the notifications attached to an order (no ownership scoping).</summary>
    Task<IReadOnlyList<OrderNotification>> RefreshNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Builds a reconciliation report of the provider's records against eShop's over a range (operator action).</summary>
    Task<ReconciliationReport> BuildReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
