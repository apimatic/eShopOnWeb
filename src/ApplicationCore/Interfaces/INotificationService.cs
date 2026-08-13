using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the messages that go out as an order moves, and the operator actions taken on them.
/// A message that cannot be sent never fails the underlying operation: the order is still placed,
/// dispatched or cancelled, and the caller's request still succeeds.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog item ids + quantities (reusing the app's existing
    /// order/order-item model), then tells the shopper their order was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken ct = default);

    /// <summary>
    /// Operator action: marks the order dispatched, tells the shopper it is on its way, and queues a
    /// "how did the delivery go?" follow-up with the provider for a few days later. Returns false if the
    /// order does not exist.
    /// </summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: cancels the order, tells the shopper, and calls off any follow-up that has not
    /// yet gone out so it never reaches them. Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>The caller's own orders, each with the notifications sent for it (statuses refreshed).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// The notifications sent for one of the caller's orders (statuses refreshed). Returns null when the
    /// order is not the caller's / does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: re-sends a message that did not reach the shopper. The idempotency key makes a
    /// repeat under the same key a no-op (returning the notification already produced), while a fresh key
    /// is a genuine new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Operator action: disposes of a message's content at the shopper's request — the text is no longer
    /// retrievable from the provider either, while the fact it was sent and its outcome survive.
    /// Returns false when the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: lines the provider's own record of messages (from this app's sending number,
    /// over a date range) up against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>One line of a place-order request: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications sent for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public enum ResendOutcome
{
    /// <summary>No notification with the given id exists.</summary>
    NotFound,
    /// <summary>A fresh resend was performed; a new notification was produced.</summary>
    Sent,
    /// <summary>A request under this idempotency key was already handled; the earlier notification is returned.</summary>
    Replayed,
    /// <summary>The message cannot be re-sent (its content has been disposed of).</summary>
    Unresendable
}

/// <summary>Outcome of a resend.</summary>
public record ResendResult(ResendOutcome Outcome, int NotificationId, string? Message)
{
    public static ResendResult NotFound() => new(ResendOutcome.NotFound, 0, null);
    public static ResendResult Sent(int notificationId) => new(ResendOutcome.Sent, notificationId, null);
    public static ResendResult Replay(int notificationId) => new(ResendOutcome.Replayed, notificationId, null);
    public static ResendResult Unresendable(int notificationId, string message) => new(ResendOutcome.Unresendable, notificationId, message);
}

/// <summary>
/// A reconciliation over a date range. Every message the provider knows about (from this app's sending
/// number) and every message eShop believes it sent is lined up by provider identifier so a message one
/// side has and the other does not is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? ProviderFrom,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus);
