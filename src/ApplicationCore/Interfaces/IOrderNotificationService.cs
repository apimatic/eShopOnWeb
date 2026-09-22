using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates orders and the SMS notifications that go out as an order moves. A message that
/// cannot be sent never fails the underlying order operation — the order is still placed,
/// dispatched or cancelled, and the caller's request still succeeds. A shopper with no number on
/// file is simply not messaged. Each action is separately invocable.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for <paramref name="buyerId"/> from catalog items, reusing the existing
    /// Order/OrderItem model. Tells the shopper their order was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> lines, Address shipToAddress, CancellationToken ct);

    /// <summary>Operator marks the order dispatched: tell the shopper, and queue a delivery follow-up a few days out.</summary>
    Task<OrderActionOutcome> DispatchOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator cancels the order: tell the shopper, and call off any follow-up that has not yet gone out.</summary>
    Task<OrderActionOutcome> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Operator re-sends a message that did not reach the shopper. Idempotent on the caller-supplied
    /// key: a repeat under the same key produces no second message. Returns the id of the
    /// notification the resend produced, or null if the notification was not found.
    /// </summary>
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, keeping the fact and outcome.
    /// Returns false if the notification was not found; throws if the provider disposal failed.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>The caller's orders, each with its notifications' current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// The notifications for one order, with each message's current provider outcome refreshed.
    /// Scoped: a non-admin caller only sees their own order's notifications (returns null if not theirs).
    /// </summary>
    Task<IReadOnlyList<SmsNotification>?> GetOrderNotificationsAsync(int orderId, string requestingBuyerId, bool isAdmin, CancellationToken ct);

    /// <summary>
    /// Reconcile the provider's own record of messages sent from the configured number over a range
    /// against what this application believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>The outcome of an operator dispatch/cancel action.</summary>
public enum OrderActionOutcome
{
    /// <summary>No order with that id exists.</summary>
    OrderNotFound = 0,

    /// <summary>The order was already in (or past) the target state — an idempotent no-op; no message sent.</summary>
    NoChange = 1,

    /// <summary>The transition happened and its notifications were attempted.</summary>
    Applied = 2
}

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public sealed record OrderLineItem(int CatalogItemId, int Quantity);

/// <summary>An order paired with its notifications (for the caller's my-orders view).</summary>
public sealed record OrderWithNotifications(Order Order, IReadOnlyList<SmsNotification> Notifications);

/// <summary>The reconciliation report over a date range.</summary>
public sealed record ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Messages present on both sides, matched by provider SID.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = new List<ReconciliationEntry>();

    /// <summary>The provider knows about these; eShop has no record of them.</summary>
    public IReadOnlyList<ReconciliationEntry> InProviderOnly { get; init; } = new List<ReconciliationEntry>();

    /// <summary>eShop believes it sent these; the provider has no record in range.</summary>
    public IReadOnlyList<ReconciliationEntry> InEShopOnly { get; init; } = new List<ReconciliationEntry>();

    /// <summary>True if the provider-side page walk hit its cap and the report may be incomplete.</summary>
    public bool Truncated { get; init; }

    public int ProviderPagesFetched { get; init; }
}

public sealed record ReconciliationEntry
{
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public int? NotificationId { get; init; }
    public string? LocalState { get; init; }
    public DateTimeOffset? ProviderDateSent { get; init; }
}
