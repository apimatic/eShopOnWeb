using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raises and tracks the messages that go out as an order moves. Every "notify" method is best-effort:
/// a message that cannot be sent is recorded but never fails the underlying order operation, and a
/// shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did the delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up that has not yet gone out.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications raised for an order, optionally refreshing each message's delivery outcome from
    /// the provider first.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating a request under the same idempotency
    /// key returns the already-produced notification without sending again.
    /// </summary>
    Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and here, while the record that it was sent and
    /// what became of it survives.
    /// </summary>
    Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lines up the provider's record of messages for a range against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
