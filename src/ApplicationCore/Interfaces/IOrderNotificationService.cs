using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves. A message that cannot be sent must
/// never fail the underlying order operation — these notify methods record the failure on a
/// <see cref="Notification"/> and return normally. A shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed. Never throws for a messaging failure.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Tell the shopper the order is on its way and queue a "how did delivery go?" follow-up with the
    /// provider for a few days later. Never throws for a messaging failure.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Tell the shopper the order was cancelled and call off any not-yet-sent follow-up so it never reaches
    /// them. Never throws for a messaging failure.
    /// </summary>
    Task NotifyOrderCanceledAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Re-send a message that did not reach the shopper, under a caller-supplied idempotency key. Repeating
    /// the same key returns the notification already produced for it (no second message); a fresh key sends
    /// again. Returns the notification the resend produced.
    /// </summary>
    Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider (so its text is no longer retrievable there) and drop
    /// the local copy, while the fact it was sent and what became of it survive.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Reconcile the provider's record of this application's messages for a range against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Refresh the provider-owned delivery state of the given notifications from the provider.</summary>
    Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken ct);
}
