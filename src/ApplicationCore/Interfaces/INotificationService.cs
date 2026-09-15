using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS a shop sends as an order moves, and the operator actions taken afterwards.
/// A message that cannot be sent NEVER fails the underlying operation: every "notify" method below
/// swallows provider failures (recording them) so the order is still placed / dispatched / cancelled.
/// A shopper with no number on file is simply not messaged.
/// </summary>
public interface INotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws for a messaging failure.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up with
    /// the provider for a few days later. Never throws for a messaging failure.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any not-yet-sent follow-up for it, so
    /// a "how did delivery go?" message can never reach them for a cancelled order. Never throws for
    /// a messaging failure.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// The notifications for one order, each with its current delivery outcome. When
    /// <paramref name="refreshFromProvider"/> is true, non-terminal messages are re-read from the
    /// provider first so the outcome is current.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken);

    /// <summary>The notifications for a set of orders (last-known outcome, no provider round-trips).</summary>
    Task<IReadOnlyList<Notification>> GetNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// idempotency key does not send a second message; a fresh key is a genuine second attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Disposes of a message's content at the provider (and locally). The fact a message was sent and
    /// what became of it survives.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>Builds a reconciliation report over [from, to] for the configured sending number.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public enum ResendOutcome
{
    /// <summary>A new message was sent.</summary>
    Created,
    /// <summary>The same idempotency key was seen before; no second message was sent.</summary>
    ReplayedIdempotent,
    /// <summary>The notification to resend does not exist.</summary>
    OriginalNotFound,
    /// <summary>The original message's content was disposed of and can no longer be resent.</summary>
    ContentDisposed
}

/// <summary>The outcome of a resend, plus the notification the resend produced (when there is one).</summary>
public record ResendResult(ResendOutcome Outcome, Notification? Notification);
