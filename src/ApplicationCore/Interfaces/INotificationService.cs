using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves, and the operator actions on
/// those messages. A message that cannot be sent never fails the underlying order operation — the
/// outcome is recorded on the notification instead. A shopper with no number on file is not messaged.
/// </summary>
public interface INotificationService
{
    /// <summary>Tells the shopper their order was placed (one message per number they have on file).</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way, and queues a "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled, and calls off any delivery follow-up that has not yet
    /// gone out so it never reaches them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper, under a caller-supplied idempotency key.
    /// A repeat under the same key sends nothing new and returns the message the first call produced.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally, keeping the record that a message
    /// was sent and what became of it. Returns false when no such notification exists.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages from the configured sending number against what
    /// eShop believes it sent, over the whole date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the delivery outcome of the given notifications from the provider (only those not yet
    /// in a terminal state), so a read reports the current state. Never throws on a provider hiccup.
    /// </summary>
    Task RefreshStatusesAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a re-send request.</summary>
public record ResendResult(ResendStatus Status, int? NotificationId);

public enum ResendStatus
{
    /// <summary>A new message was sent; <c>NotificationId</c> is the message it produced.</summary>
    Created,
    /// <summary>The idempotency key was already used; <c>NotificationId</c> is the original result — nothing was sent.</summary>
    Duplicate,
    /// <summary>No notification with the given id exists.</summary>
    NotFound,
    /// <summary>The message cannot be re-sent (e.g. its content has been disposed of).</summary>
    CannotResend
}

/// <summary>
/// A reconciliation report over a date range: the provider's record of messages from the configured
/// sending number lined up against eShop's own notifications.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>
/// One line of a reconciliation report. Destination numbers are deliberately omitted — a shopper's
/// number is never exposed here.
/// </summary>
public record ReconciliationEntry(string? Sid, int? NotificationId, string? ProviderStatus, string? EShopStatus);
