using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS messages that go out as an order moves. A message that cannot be
/// sent never fails the underlying operation: the order is still placed, dispatched or cancelled.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for a shopper from catalog item ids and quantities, reusing the app's existing
    /// order model, and tell the shopper their order was placed.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark an order dispatched (operator action). The shopper is told it is on its way and a
    /// follow-up asking how delivery went is queued with the provider for a few days later. Returns
    /// null if the order does not exist.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel an order (operator action). The shopper is told, and any follow-up not yet sent is
    /// called off at the provider so it can never reach them. Returns null if the order does not
    /// exist.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's orders, each with its notifications and where they got to. Delivery outcomes are
    /// refreshed from the provider for messages that are not yet in a terminal state.
    /// </summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order, with delivery outcomes refreshed from the provider. When
    /// <paramref name="restrictToBuyerId"/> is supplied the order must belong to that shopper, else
    /// null is returned. Pass null for operator callers.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string? restrictToBuyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper (operator action). The idempotency key makes
    /// a repeat of the same request a no-op that returns the already-produced notification, while a
    /// fresh key is a genuine new attempt. Returns a result describing what happened.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally (operator action). Returns false if
    /// the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcile the provider's own record of messages sent from this application's number over a
    /// date range against what eShop believes it sent (operator action).
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One line of an order placement request: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications raised for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>Outcome of a resend.</summary>
public record ResendResult(ResendOutcome Outcome, OrderNotification? Notification, string? Error)
{
    public static ResendResult NotFound() => new(ResendOutcome.NotFound, null, null);
    public static ResendResult NotEligible(string error) => new(ResendOutcome.NotEligible, null, error);
    public static ResendResult Sent(OrderNotification notification) => new(ResendOutcome.Sent, notification, null);
    public static ResendResult Duplicate(OrderNotification notification) => new(ResendOutcome.Duplicate, notification, null);
}

public enum ResendOutcome
{
    Sent,
    Duplicate,
    NotEligible,
    NotFound
}

/// <summary>
/// The result of a reconciliation run: messages the provider and eShop agree on, plus the two kinds
/// of disagreement.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>A message present in both the provider's record and eShop's.</summary>
public record ReconciliationMatch(string MessageSid, string ProviderStatus, string EShopStatus, int? NotificationId);

/// <summary>A message known to only one side.</summary>
public record ReconciliationEntry(string MessageSid, string? Status, int? NotificationId, DateTimeOffset? DateSent);
