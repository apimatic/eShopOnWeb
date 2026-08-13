using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public record OrderLineItem(int CatalogItemId, int Quantity);

/// <summary>The result of an operator re-send.</summary>
public class ResendResult
{
    /// <summary>The notification the re-send produced (or the one already produced under the same idempotency key).</summary>
    public required OrderNotification Notification { get; init; }

    /// <summary>True when the idempotency key had already been used, so no new message was sent.</summary>
    public bool ReusedExisting { get; init; }
}

/// <summary>
/// Drives the SMS notifications that accompany an order as it moves, and the operator actions on those
/// messages. A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the given buyer from catalog items, reusing the app's existing order model,
    /// and tells the shopper their order was placed.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineItem> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order dispatched: tells the shopper it is on its way and queues the delayed
    /// "how did the delivery go?" follow-up with the provider. Returns false if the order does not exist.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order: tells the shopper, and calls off any follow-up that has not yet gone out so it
    /// never reaches them. Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications raised for an order and what became of each. When <paramref name="refreshFromProvider"/>
    /// is true, non-terminal messages have their delivery outcome refreshed from the provider first.
    /// Returns null when the order does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper, under a caller-supplied idempotency key.
    /// Repeating a request under the same key does not send a second message. Returns null when the
    /// target notification does not exist.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and here, while the record that it was sent and
    /// its outcome survive. Returns false when the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Produces a reconciliation report over a date range for the configured sending number.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
