using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that accompany an order as it moves. Every method is
/// best-effort with respect to messaging: a message that cannot be sent is recorded as such and
/// never surfaces as a failure of the order operation that triggered it. A shopper with no number
/// on file is simply not messaged.
/// </summary>
public interface INotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper their order is on its way and queue the "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper their order was cancelled and call off the pending delivery follow-up so it
    /// never reaches them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// a repeat under the same key returns the notification the first attempt produced without sending
    /// again; a fresh key sends anew. Returns the notification the resend produced (existing or new).
    /// </summary>
    Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, while the fact it was sent and what
    /// became of it survive.
    /// </summary>
    Task<Notification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refresh each non-terminal notification's delivery outcome from the provider.</summary>
    Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default);

    /// <summary>Build the provider-vs-eShop reconciliation report for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
