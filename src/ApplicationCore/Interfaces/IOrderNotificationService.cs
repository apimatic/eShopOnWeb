using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that accompany an order's lifecycle,
/// plus the operator actions over those messages. A message that cannot be sent never
/// fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog items, reusing the app's order model,
    /// and tells the shopper (on every number they have on file) that it was placed.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a
    /// "how did delivery go" follow-up with the provider for a few days later.
    /// </summary>
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, calls off any not-yet-sent follow-up with the provider, and
    /// tells the shopper it was cancelled.
    /// </summary>
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same
    /// idempotency key returns the already-produced message without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts the body at the provider and clears it
    /// locally, while the fact it was sent and what became of it survives.
    /// </summary>
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the provider's own record of messages sent from this application's
    /// configured number over a range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the provider's current delivery outcome for the given notifications.</summary>
    Task RefreshDeliveryStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Outcome of a resend request.</summary>
public record ResendResult(bool Found, bool ContentDisposed, OrderNotification? Notification)
{
    public static ResendResult NotFound() => new(false, false, null);
    public static ResendResult Disposed() => new(true, true, null);
    public static ResendResult Ok(OrderNotification notification) => new(true, false, notification);
}

/// <summary>One line of the reconciliation report: a message seen on one or both sides.</summary>
public record ReconciliationEntry(
    string Sid,
    bool InProvider,
    bool InEShop,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> InProviderNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInProvider,
    IReadOnlyList<ReconciliationEntry> Matched);
