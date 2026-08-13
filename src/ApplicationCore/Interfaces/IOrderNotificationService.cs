using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS order-notification capability: a shopper's contact numbers, the messages
/// that go out as an order moves, and the operator actions taken on those messages. Sending is always
/// best-effort — a message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    // ---- Flow 1: contact numbers (shopper-scoped) ----
    Task<ContactNumber> RegisterContactNumberAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<bool> DeleteContactNumberAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);

    // ---- Flow 2: order lifecycle ----
    Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default);
    /// <summary>Marks an order dispatched. Returns null if no such order exists.</summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);
    /// <summary>Cancels an order. Returns null if no such order exists.</summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each with the current state of its notifications.</summary>
    Task<IReadOnlyList<OrderNotificationsView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default);
    /// <summary>
    /// The notifications raised for one order, refreshed against the provider. Scoped to the caller:
    /// returns null when the order does not exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string ownerId, CancellationToken cancellationToken = default);

    // ---- Flow 3: operator actions ----
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// a repeat under the same key returns the message the first attempt produced without sending again.
    /// Returns null when the notification does not exist.
    /// </summary>
    Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    /// <summary>Disposes of a message's content at the provider and locally. Returns null when the notification does not exist.</summary>
    Task<Notification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the current state of its notifications.</summary>
public record OrderNotificationsView(Order Order, IReadOnlyList<Notification> Notifications);

/// <summary>The result of a re-send: the message it produced and whether it was a fresh send or an idempotent replay.</summary>
public record ResendOutcome(Notification Notification, bool WasReplayed);

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent,
/// over a date range, for this application's configured sending number.
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
    string? ProviderStatus,
    string? EShopStatus,
    int? OrderId,
    NotificationKind? Kind,
    DateTimeOffset? DateSent);
