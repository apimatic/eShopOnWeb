using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order together with the notifications recorded for it (and where each of them got to).</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);

/// <summary>One message the provider and eShop agree on, with each side's view of its status.</summary>
public record ReconciliationMatch(string Sid, string? ProviderStatus, int NotificationId, string? LocalStatus);

/// <summary>
/// A reconciliation over a date range: the provider's record of messages from the configured sending
/// number, lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderMessage> OnlyAtProvider,
    IReadOnlyList<Notification> OnlyInEShop);

/// <summary>
/// Flows 2 and 3 — the messages that go out as an order moves, and the operator actions on them.
/// Order-lifecycle transitions (place / dispatch / cancel) always succeed for the underlying order;
/// a message that cannot be sent is recorded, never thrown.
/// </summary>
public interface INotificationService
{
    // Flow 2 — messages as the order moves
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>Marks an order dispatched (operator). Null if the order does not exist. Throws on an invalid transition.</summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels an order (operator), calling off any not-yet-sent follow-up. Null if the order does not exist.</summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>The notifications for one order, scoped to its owner. Null if the order does not exist or is not the caller's.</summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    // Flow 3 — operator actions

    /// <summary>Re-sends a message (operator), idempotent on the caller-supplied key. Null if the source notification does not exist.</summary>
    Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<Notification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
