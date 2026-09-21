using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-progress SMS notifications: keeping shoppers' numbers, sending the messages as an
/// order moves, and the operator actions over them. A message that cannot be sent never fails the
/// underlying operation, and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    // ---- Flow 1: the shopper's contact number ----
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct);
    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct);
    Task<bool> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct);

    // ---- Flow 2: messages as the order moves ----
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct);
    Task<OrderTransition> DispatchOrderAsync(int orderId, CancellationToken ct);
    Task<OrderTransition> CancelOrderAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Notifications for an order the caller owns; null when the order does not exist or is not theirs.</summary>
    Task<IReadOnlyList<OrderNotification>?> GetOwnedOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);

    // ---- Flow 3: operator actions ----
    /// <summary>Re-send a message under a caller-supplied idempotency key; null when the notification does not exist.</summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Dispose of a message's content at the provider and locally; false when the notification does not exist.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>The outcome of an order lifecycle transition.</summary>
public enum OrderTransition
{
    /// <summary>No order with that id.</summary>
    NotFound = 0,
    /// <summary>The order was already in a state that made the transition a no-op — nothing sent.</summary>
    NoChange = 1,
    /// <summary>The order transitioned and the associated messages were raised.</summary>
    Changed = 2
}

/// <summary>An order together with the notifications raised about it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>
/// The reconciliation of the provider's own record of messages against what eShop believes it sent, over a
/// date range. Both sides are filtered on the provider's send time (the same clock).
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly,
    IReadOnlyList<ReconciliationEntry> OutOfWindow);

/// <summary>One line of the reconciliation: a provider message, an eShop notification, or both.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopState,
    DateTimeOffset? ProviderDateSent);
