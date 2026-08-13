using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives the order lifecycle and the messages that accompany it. A message that cannot be sent never
/// fails the underlying order operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper (reusing the app's order model) and texts them that it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, texts the shopper it is on its way, and queues a follow-up with the
    /// provider for a few days later. Returns null if the order does not exist.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, texts the shopper, and calls off any follow-up that has not yet gone out.
    /// Returns null if the order does not exist.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// a repeat under the same key returns the message the first attempt produced without sending again.
    /// Returns null if the source notification does not exist.
    /// </summary>
    Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts it at the provider and clears it here, while the record
    /// of the message and its outcome survives. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Builds a reconciliation report for the application's sending number over a date range.</summary>
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the order's notifications, first refreshing any that are not yet in a terminal delivery
    /// state from the provider so each carries its current outcome. Provider read failures are ignored.
    /// </summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);
}
