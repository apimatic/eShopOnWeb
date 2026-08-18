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
/// Orchestrates order lifecycle transitions and the messages that go out as an order moves. A
/// message that cannot be sent never fails the underlying operation; a shopper with no number on
/// file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog items for the shopper and tells them it was placed.</summary>
    Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks the order dispatched, tells the shopper, and queues a "how did delivery go"
    /// follow-up with the provider for a few days later.</summary>
    Task<Result> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order, tells the shopper, and calls off any follow-up that has not yet
    /// gone out so it never reaches them.</summary>
    Task<Result> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Re-sends a message that did not reach the shopper. Repeats under the same
    /// idempotency key do not send again; a fresh key is a legitimate second attempt. Returns the
    /// notification the resend produced.</summary>
    Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content at the provider so its text is no longer
    /// retrievable, while the fact it was sent and its outcome survive.</summary>
    Task<Result> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>The notifications raised for an order (with refreshed delivery outcomes), scoped to
    /// the requesting shopper who must own the order.</summary>
    Task<Result<IReadOnlyList<OrderNotification>>> GetOrderNotificationsAsync(int orderId,
        string requestingBuyerId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each with the notifications raised for it.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's record of messages from the configured sending number
    /// against what eShop believes it sent, over the whole date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
