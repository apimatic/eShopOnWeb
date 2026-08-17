using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and keeps shoppers informed by SMS as their orders move. A message that cannot be
/// sent never fails the underlying operation: the order is still placed, dispatched or cancelled and
/// the caller's request still succeeds. A shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog item ids and quantities (reusing the existing
    /// order/order-item model) and tells them their order was placed.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken cancellationToken = default);

    /// <summary>Fetches an order, or null if it does not exist.</summary>
    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's orders.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a "how did the
    /// delivery go?" follow-up with the provider for a few days later. Returns null if not found.
    /// </summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off the not-yet-sent follow-up so it never
    /// reaches them. Returns null if not found.
    /// </summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The notifications sent for an order, with each one's current provider outcome.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>All notifications for the shopper's orders, with current provider outcomes.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Fetches a single notification, or null if it does not exist.</summary>
    Task<OrderNotification?> GetNotificationAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same idempotency key
    /// does not send a second message; a fresh key is a genuine second attempt.
    /// </summary>
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts the body at the provider and clears the stored copy,
    /// while the fact a message was sent and what became of it survives.
    /// </summary>
    Task<ContentDisposalOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Builds the provider-vs-eShop reconciliation report for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
