using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A message that cannot be
/// sent never fails the underlying operation — the order is still placed, dispatched or cancelled.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog items, reusing the app's existing Order/OrderItem
    /// model, then tells the shopper their order was placed. Returns null when a requested catalog item
    /// does not exist.
    /// </summary>
    Task<Order?> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Marks the order dispatched: tells the shopper it is on its way and queues a follow-up asking how the
    /// delivery went a few days later with the provider.
    /// </summary>
    Task<OrderTransition> DispatchAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Cancels the order: tells the shopper, and calls off any follow-up that has not yet gone out so it can
    /// never reach them.
    /// </summary>
    Task<OrderTransition> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>The shopper's own orders, each with the current state of its notifications.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string ownerId, CancellationToken ct = default);

    /// <summary>
    /// The notifications for one of the shopper's own orders, each refreshed against the provider. Returns
    /// null when the order does not belong to this shopper (or does not exist).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string ownerId, int orderId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: re-sends a message that did not reach the shopper. Repeating the request under the
    /// same idempotency key does not send a second message; a fresh key is a genuine new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Operator action: disposes of a message's content at the provider and clears it here, while the record
    /// that a message was sent and what became of it survives. Returns false when no such notification exists.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: lines up the provider's own record of messages sent from the configured sending
    /// number over a date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>A requested order line: how many of a catalog item to order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public enum OrderTransitionOutcome { Success, OrderNotFound, AlreadyDispatched, AlreadyCancelled }

/// <summary>Result of a dispatch/cancel transition.</summary>
public record OrderTransition(OrderTransitionOutcome Outcome, Order? Order);

/// <summary>An order paired with the notifications sent about it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public enum ResendOutcome { Sent, AlreadyProcessed, NotificationNotFound, DestinationRemoved, ContentDisposed }

/// <summary>Result of a resend attempt. <see cref="Notification"/> is the (new or existing) message the resend resolved to.</summary>
public record ResendResult(ResendOutcome Outcome, OrderNotification? Notification);

public enum ReconciliationState { InSync, ProviderOnly, EShopOnly }

/// <summary>One line of a reconciliation report, lining a provider record up against an eShop record.</summary>
public record ReconciliationEntry(
    string? Sid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus,
    ReconciliationState State);

/// <summary>A reconciliation report over a date range, counting only the configured sending number's messages.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    int ProviderMessageCount,
    int EShopMessageCount,
    int InSyncCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
