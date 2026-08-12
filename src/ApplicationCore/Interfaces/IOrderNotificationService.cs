using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the app's existing order/order-item model) and drives the SMS messages
/// that go out as an order moves. A message that cannot be sent never fails the underlying
/// operation, and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog item ids and quantities, then tells them it was
    /// placed. Returns the new order's id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, PlaceOrderInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order dispatched (operator action): tells the shopper it is on its way and queues a
    /// delivery follow-up with the provider for a few days later. Returns false if the order does not exist.
    /// </summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order (operator action): tells the shopper and calls off any delivery follow-up
    /// that has not yet gone out. Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own orders, each with the notifications sent about it and where they got to.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications sent for one of the shopper's own orders, with each message's current
    /// delivery outcome. Returns null when the order does not exist or does not belong to the shopper.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper (operator action). Repeating the request
    /// under the same idempotency key sends nothing new and returns the original result.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the shopper's request (operator action): redacts the body
    /// at the provider and locally, while the fact of the send and its outcome survive. Returns false
    /// if the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the provider's record of messages from this application's sending number against
    /// eShop's own records over a date range (operator action).
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
