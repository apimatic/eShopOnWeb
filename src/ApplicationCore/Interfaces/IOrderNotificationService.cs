using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Result of a resend attempt, distinguishing a genuine new send from an idempotent replay.</summary>
public record ResendResult(bool Succeeded, int? NotificationId, bool Duplicate, string? Error);

/// <summary>One message lined up between the provider's record and eShop's record.</summary>
public record ReconciliationEntry(
    string ProviderSid,
    int? NotificationId,
    int? OrderId,
    string? LocalStatus,
    string? ProviderStatus,
    DateTimeOffset? DateSent);

/// <summary>
/// A reconciliation report over a date range: the provider's own record of messages from the
/// application's configured sending number, lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FromNumber,
    int ProviderCount,
    int LocalCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly);

/// <summary>
/// Orchestrates the messages that go out as an order moves, and the operator actions on them.
/// A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up
    /// with the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any delivery follow-up that has
    /// not yet gone out.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Returns the notifications for an order, with delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the delivery outcome of the given notifications from the provider.</summary>
    Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. A repeat under the same idempotency
    /// key does not send again; a fresh key is a genuine new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally, while preserving the fact
    /// that it was sent and what became of it. Returns false if the notification is not found.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Builds a reconciliation report over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
