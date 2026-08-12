using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives an order through its lifecycle and the SMS notifications that go out as it moves. A message that
/// cannot be sent never fails the underlying order operation; a shopper with no number on file is simply
/// not messaged.
/// </summary>
public interface IOrderMessagingService
{
    /// <summary>
    /// Place an order for the shopper from catalog item ids and quantities, reusing the app's order model.
    /// The shopper is told (best-effort) that the order was placed. Throws
    /// <see cref="Exceptions.InvalidOrderRequestException"/> for an empty request or an unknown item.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark the order dispatched. Tells the shopper it is on its way and queues a
    /// follow-up with the provider for a few days later. Returns null if the order does not exist; throws
    /// <see cref="Exceptions.InvalidOrderStatusTransitionException"/> if it cannot be dispatched.
    /// </summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel the order. Calls off any not-yet-sent follow-up at the provider so it can
    /// never reach the shopper, then tells the shopper it was cancelled. Returns null if the order does not
    /// exist; throws <see cref="Exceptions.InvalidOrderStatusTransitionException"/> if it cannot be cancelled.
    /// </summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's orders, each with its notifications (delivery outcomes refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order owned by the shopper, delivery outcomes refreshed from the provider.
    /// Returns null when the order does not exist or is owned by someone else.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. Idempotent on
    /// <paramref name="idempotencyKey"/> — a repeat under the same key returns the earlier result without
    /// sending again. Returns null if the target notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: dispose of a message's content — redact it at the provider and clear it here — while
    /// the record that it was sent, and what became of it, survives. Returns false if it does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: reconcile the provider's own record of messages sent from the configured number in a
    /// date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications that went out about it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>How a single message lined up between the provider and eShop.</summary>
public enum ReconciliationOutcome
{
    /// <summary>Both the provider and eShop have this message.</summary>
    Matched = 0,
    /// <summary>The provider has it but eShop has no record of it.</summary>
    MissingInEShop = 1,
    /// <summary>eShop believes it sent it but the provider's range does not include it.</summary>
    MissingAtProvider = 2
}

/// <summary>One reconciled message.</summary>
public record ReconciliationEntry(
    string Sid,
    ReconciliationOutcome Outcome,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId);

/// <summary>The reconciliation over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingAtProviderCount,
    IReadOnlyList<ReconciliationEntry> Entries);
