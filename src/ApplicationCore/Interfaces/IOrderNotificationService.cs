using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS a shopper receives as their order moves. Every notify method is best-effort:
/// a message that cannot be sent is recorded and never fails the underlying order operation, and a shopper
/// with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a "how did the delivery go?" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out so it
    /// never reaches them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Repeating under the same idempotency key returns the
    /// message the first call produced without sending again; a fresh key is a genuine new attempt.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Dispose of a message's content at the provider and locally, keeping the record it was sent.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>Reconcile the provider's record of messages for the configured sending number against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// <summary>Refresh each notification for an order from the provider's current delivery status, and return them.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken);
}
