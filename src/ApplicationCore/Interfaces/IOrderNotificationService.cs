using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS messages that go out as an order moves, and the operator actions on those
/// messages. Sending is best-effort: a message that cannot be sent never fails the underlying
/// order operation, and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order);

    /// <summary>Tells the shopper their order is on its way and queues a delivery-feedback follow-up
    /// with the provider for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(Order order);

    /// <summary>Tells the shopper their order was cancelled and calls off any not-yet-sent
    /// delivery-feedback follow-up so it can never reach them.</summary>
    Task NotifyOrderCancelledAsync(Order order);

    /// <summary>Re-sends the message identified by <paramref name="notificationId"/>. Repeating the
    /// request under the same <paramref name="idempotencyKey"/> returns the message the first attempt
    /// produced without sending a second; a fresh key produces a genuine new send.</summary>
    Task<Notification> ResendAsync(int notificationId, string idempotencyKey);

    /// <summary>Disposes of a message's content at the shopper's request — locally and at the provider —
    /// while the fact of the send and its outcome survive.</summary>
    Task DisposeContentAsync(int notificationId);

    /// <summary>The messages sent for an order, each refreshed to its current provider outcome.</summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId);

    /// <summary>Loads a single notification by id (no refresh), or null if it does not exist.</summary>
    Task<Notification?> FindNotificationAsync(int notificationId);

    /// <summary>Lines up the provider's own record of messages against what eShop believes it sent,
    /// over the whole date range, counting only the application's configured sending number.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
