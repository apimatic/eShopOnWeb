using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the messages that go out as an order moves and keeps the operator's view of what actually
/// reached the shopper. A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed. Best-effort: never throws.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way and queue a "how did the delivery go" follow-up with
    /// the provider for a few days later. Best-effort: never throws.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled and call off any delivery follow-up that has not yet
    /// gone out, so it never reaches them. Best-effort: never throws.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// The messages for an order, each with its current delivery outcome refreshed from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The messages for a set of orders, each with its current delivery outcome refreshed.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(int[] orderIds, CancellationToken cancellationToken = default);

    /// <summary>Look up a single message by its identifier.</summary>
    Task<OrderNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Idempotent on the caller-supplied key: a
    /// repeat under the same key returns the message already produced without sending another; a fresh
    /// key produces a new message.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, keeping the fact of the message and
    /// its outcome. Returns false if the message does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line the provider's own record of messages sent from the configured number in a range up against
    /// what this application believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Call off any message still queued for a future send to a shopper's number.</summary>
    Task CancelScheduledMessagesToNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a re-send.</summary>
public record ResendResult(bool Found, OrderNotification? Notification, bool Reused)
{
    public static ResendResult NotFound() => new(false, null, false);
    public static ResendResult Sent(OrderNotification notification) => new(true, notification, false);
    public static ResendResult AlreadyProcessed(OrderNotification notification) => new(true, notification, true);
}

/// <summary>A local message lined up with the provider's record of the same message.</summary>
public record ReconciliationMatch(OrderNotification Local, ProviderMessage Provider);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderMessage> ProviderOnly,
    IReadOnlyList<OrderNotification> EShopOnly);
