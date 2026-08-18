using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the app's Order model) and drives the SMS notifications that go out as an order
/// moves. A messaging failure never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the shopper from catalog lines and notify them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark an order dispatched (operator): tell the shopper it is on its way and queue a follow-up asking
    /// how the delivery went, with the provider, a few days later. Returns null if the order does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel an order (operator): tell the shopper, and call off any follow-up that has not yet gone out.
    /// Returns null if the order does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>All of a shopper's notifications, with delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A shopper's notifications for one of their own orders, refreshed. Returns null if the order is not theirs.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Load one notification by id (operator scope). Null if it does not exist.</summary>
    Task<OrderNotification?> GetNotificationAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator re-send of a message that did not reach the shopper. Idempotent on
    /// <paramref name="idempotencyKey"/>: a repeat under the same key returns the notification the first
    /// request produced without sending again. Returns null if the notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider (operator). Returns false if the notification does not
    /// exist. Throws <see cref="Exceptions.SmsProviderException"/> if the provider could not carry it out.
    /// </summary>
    Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconcile the provider's record of sent messages against eShop's for a date range (operator).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
