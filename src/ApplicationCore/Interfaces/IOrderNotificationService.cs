using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves. Every method here is
/// best-effort with respect to the shopper being messaged: a message that cannot be sent is
/// recorded as such and never surfaced as a failure of the underlying order operation, and a
/// shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper their order is on its way, and queues a delivery follow-up
    /// with the provider for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper their order was cancelled, and calls off any delivery
    /// follow-up that has not yet gone out.</summary>
    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// <paramref name="idempotencyKey"/> returns the message the first attempt produced without
    /// sending another; a fresh key sends a new one. Returns null if the notification does not
    /// exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts it at the provider and clears it here, while
    /// keeping the record of the message and its outcome. Returns null if it does not exist.
    /// </summary>
    Task<OrderNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the notifications for an order, each refreshed against the provider so its
    /// reported delivery outcome is current.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the given notifications' delivery outcomes against the provider.</summary>
    Task RefreshDeliveryStateAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages sent from the configured sending number
    /// over a range against what this application believes it sent, so discrepancies in either
    /// direction are visible.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
