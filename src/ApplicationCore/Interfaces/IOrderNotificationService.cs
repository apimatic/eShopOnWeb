using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. Every messaging
/// action here is best-effort: a message that cannot be sent never fails the underlying order
/// operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog items, then texts them that it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, texts the shopper it is on its way, and queues a "how did
    /// delivery go?" follow-up with the provider for a few days later. Returns null if no such order.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, texts the shopper, and calls off any follow-up that has not yet gone out.
    /// Returns null if no such order.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's orders, each with its notifications (statuses refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order, statuses refreshed from the provider. When <paramref name="buyerScope"/>
    /// is supplied the order must belong to that shopper; otherwise null is returned.
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string? buyerScope, CancellationToken cancellationToken = default);

    /// <summary>Re-sends a message that did not reach the shopper, idempotent on the supplied key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content at the provider and locally. Returns false if no such notification.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's record of messages from this application's number against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
