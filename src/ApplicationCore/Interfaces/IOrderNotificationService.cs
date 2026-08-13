using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order lifecycle and the SMS messages it produces. A message that cannot
/// be sent never fails the underlying order operation; a shopper with no number on file is
/// simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order from catalog items for the shopper, then tell them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark the order dispatched, tell the shopper it is on its way, and queue
    /// the "how did delivery go?" follow-up with the provider for a few days later.
    /// </summary>
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel the order, tell the shopper, and call off the follow-up that has
    /// not yet gone out so it can never reach them.
    /// </summary>
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's own orders, each with its notifications and where they got to. Refreshes
    /// delivery outcomes from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What was sent for one of the caller's orders and what became of each message. Refreshes
    /// delivery outcomes from the provider. Throws <see cref="Exceptions.EntityNotFoundException"/>
    /// if the order is not the caller's.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. Repeating a request
    /// under the same <paramref name="idempotencyKey"/> returns the message already produced
    /// rather than sending another; a fresh key sends a new one.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: dispose the content of a message about a shopper so its text is no
    /// longer retrievable from the provider, while the fact it was sent survives.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: line up the provider's own record of messages from this application's
    /// configured sending number, over a date range, against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: how many of a catalog item.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with its notification records.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>
/// One line of the reconciliation report. Deliberately omits the destination number so the
/// report never exposes a shopper's number.
/// </summary>
public record ReconciliationEntry(
    string Sid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    bool KnownToProvider,
    bool KnownToEShop,
    int? NotificationId,
    int? OrderId,
    string? Kind,
    string? EShopStatus);

/// <summary>The reconciliation result for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
