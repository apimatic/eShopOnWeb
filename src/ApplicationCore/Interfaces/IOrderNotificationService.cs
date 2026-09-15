using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the messages that go out as an order moves and keeps a record of what became of each. A message
/// that cannot be sent never fails the underlying order operation; a shopper with no number on file is
/// simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed. Called as part of placing the order.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark the order (by id) dispatched — tell the shopper it is on its way and queue a
    /// "how did delivery go?" follow-up with the provider for a few days later. Returns false if no such order.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel the order (by id) — tell the shopper, and call off any follow-up that has not
    /// yet gone out so it never reaches them. Returns false if no such order.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order, but only if the order belongs to <paramref name="ownerId"/>. Each
    /// carries its current delivery outcome refreshed from the provider. Returns null when the order does not
    /// exist or is not the caller's.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForOwnerAsync(int orderId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's orders, each with its notifications and their current delivery outcomes refreshed from the
    /// provider — the data behind "my orders, showing where each notification got to".
    /// </summary>
    Task<IReadOnlyList<OwnerOrderSummary>> GetOwnerOrderSummariesAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Repeating the request under the same idempotency key
    /// returns the first result rather than sending again; a fresh key is a legitimate new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string? idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, while the fact it was sent and what became
    /// of it survives. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line the provider's own record of messages from the configured sender over a range up against what
    /// eShop believes it sent, so a message either side knows about and the other does not is visible.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>An order together with the notifications sent for it, for the "my orders" view.</summary>
public record OwnerOrderSummary(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>Outcome of a resend. On success carries the id of the message the resend produced.</summary>
public record ResendResult(bool Found, int? NotificationId, string? Status, bool Reused)
{
    public static ResendResult NotFound() => new(false, null, null, false);
}

/// <summary>A reconciliation report over a date range, keyed by provider message identifier.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of the reconciliation report. Never carries a phone number.</summary>
public record ReconciliationEntry(
    string ProviderMessageSid,
    string? ProviderStatus,
    string? EShopStatus,
    int? OrderId,
    int? ErrorCode);
