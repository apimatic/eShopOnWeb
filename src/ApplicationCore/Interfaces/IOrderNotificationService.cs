using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-lifecycle SMS notifications. A message that cannot be sent never fails the
/// underlying operation — the order is still placed, dispatched or cancelled, and the record of the
/// attempt survives.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for the shopper from catalog items (reusing the existing Order/OrderItem model),
    /// then tell the shopper it was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way and queue a "how did delivery go" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task DispatchOrderAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Call off any not-yet-sent follow-up for the order (so it can never reach the shopper) and tell
    /// the shopper the order was cancelled.
    /// </summary>
    Task CancelOrderAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// a repeat under the same key returns the same result without sending again. Returns the id of the
    /// notification the resend produced.
    /// </summary>
    Task<int> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, keeping the record that it was sent
    /// and what became of it.
    /// </summary>
    Task DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load an order's notifications, refreshing each non-final delivery outcome from the provider first
    /// (the only way to learn what happened, since the provider cannot call back into this application).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line up the provider's own record of messages sent from this application's configured number
    /// against what eShop believes it sent, across the whole date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>The result of lining up provider records against eShop's own for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SenderNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One reconciled message. Numbers/bodies are intentionally excluded.</summary>
public record ReconciliationEntry(
    string Sid,
    string? ProviderStatus,
    int? ErrorCode,
    int? NotificationId,
    int? OrderId,
    DateTimeOffset? DateSent);
