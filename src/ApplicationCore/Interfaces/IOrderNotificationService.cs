using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves, and the operator actions on
/// them. Every notify method is best-effort: a message that cannot be sent is recorded as such and
/// never surfaces as an exception to the caller, so the underlying order operation still succeeds.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed (one message per number on file).</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue the "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Repeating under the same
    /// <paramref name="idempotencyKey"/> returns the notification produced the first time without
    /// sending again (with <see cref="ResendResult.Replayed"/> set). Returns null if the original
    /// notification does not exist.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider. Returns the updated notification, or null if
    /// it does not exist.
    /// </summary>
    Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh each non-terminal notification's delivery outcome from the provider, persisting any
    /// change. Provider hiccups are swallowed so a read never fails.
    /// </summary>
    Task RefreshDeliveryStatesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcile the provider's own record of messages from the configured sending number over
    /// [<paramref name="from"/>, <paramref name="to"/>] against what this application believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a resend: the resulting notification and whether it was an idempotent replay.</summary>
public record ResendResult(OrderNotification Notification, bool Replayed);

/// <summary>A single message lined up across the provider's record and this application's record.</summary>
public record ReconciliationEntry(
    string Sid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    NotificationType? NotificationType,
    int? OrderId,
    NotificationStatus? EShopStatus);

/// <summary>
/// The result of a reconciliation over a date range. Messages the provider knows about and eShop
/// does not appear in <see cref="ProviderOnly"/>; the reverse appears in <see cref="EShopOnly"/>.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
