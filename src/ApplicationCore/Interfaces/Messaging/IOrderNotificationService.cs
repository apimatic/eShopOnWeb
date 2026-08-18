using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>The notification produced by a resend, and whether it was a fresh send or an idempotent replay.</summary>
public record ResendResult(OrderNotification Notification, bool WasReplay);

/// <summary>
/// Orchestrates the SMS notifications that accompany an order's lifecycle and the operator actions over
/// them. Sending is best-effort: a message that cannot go out is recorded but never fails the underlying
/// order operation, and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a "how did the delivery go?" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out so it
    /// never reaches them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh the provider's current delivery outcome onto the given notifications (those still pending),
    /// persisting any change, so reads reflect where each message actually got to.
    /// </summary>
    Task RefreshDeliveryStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: re-send a message that did not reach the shopper. The idempotency key makes a repeat under
    /// the same key a no-op that returns the same result; a fresh key sends again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator: dispose of a message's content at the shopper's request, so the text is no longer
    /// retrievable from the provider, while the record that a message was sent (and its outcome) survives.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Operator: reconcile the provider's ledger against eShop's records over a date-time range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
