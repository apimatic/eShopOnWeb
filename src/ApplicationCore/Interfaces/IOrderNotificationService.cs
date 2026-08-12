using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves. A message that cannot be sent never
/// fails the underlying order operation: these methods record the outcome and return normally rather than
/// throwing. A shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct);

    /// <summary>Tell the shopper the order is on its way and queue a delivery follow-up a few days out.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct);

    /// <summary>Tell the shopper the order was cancelled and call off any follow-up that has not yet gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct);

    /// <summary>
    /// The notifications raised for an order, each with its delivery outcome refreshed from the provider's
    /// current word so callers see where a message actually got to.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Repeating the request under the same idempotency key
    /// returns the earlier result and sends nothing; a fresh key is a genuine new attempt. Returns null if no
    /// such notification exists.
    /// </summary>
    Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, keeping the record that it was sent and what
    /// became of it. Returns false if no such notification exists.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>
    /// Line up the provider's own record of messages from this application's sending number against what eShop
    /// believes it sent, over a date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>The result of a re-send request.</summary>
/// <param name="NotificationId">The id of the notification the resend produced (or the earlier one under the same key).</param>
/// <param name="AlreadyProcessed">True when the idempotency key had already been used, so nothing new was sent.</param>
public record ResendOutcome(int NotificationId, bool AlreadyProcessed);

/// <summary>A reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopMessageCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report. Carries no phone number.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus);
