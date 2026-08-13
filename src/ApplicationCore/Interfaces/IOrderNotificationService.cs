using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A message that
/// cannot be sent never fails the underlying operation — the order is still placed, dispatched or
/// cancelled, and the caller's request still succeeds. A shopper with no number on file is simply
/// not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog item ids and quantities, reusing the app's
    /// existing order/order-item model, then tells the shopper it was placed. Returns the new order.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with
    /// the provider for a few days later. Returns false if the order does not exist.
    /// </summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not gone out yet.
    /// Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each with where its notifications got to (refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order, refreshed from the provider. Returns null when the order
    /// does not exist or the caller is a shopper who does not own it (operators may view any order).
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string callerId, bool callerIsAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key makes a repeated
    /// request under the same key a no-op (returning the message the first attempt produced), while
    /// a fresh key sends a genuine second attempt. Returns null when no such notification exists.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content — redacting it at the provider so its text is no longer
    /// retrievable there either — while the record that a message was sent, and what became of it,
    /// survives. Returns false when no such notification exists.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's record of messages from the configured sender against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One line of an order request: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
