using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves, and the operator actions
/// taken on them. A message that cannot be sent never fails the underlying operation: the order
/// is still placed, dispatched or cancelled and the caller's request still succeeds.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for the shopper from catalog items, reusing the app's existing order model,
    /// and tell the shopper it was placed. Returns the created order (its id is the caller's handle).
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark an order dispatched: tell the shopper it is on its way and queue a follow-up asking how
    /// the delivery went, a few days later, with the provider. Returns false when the order does
    /// not exist.
    /// </summary>
    Task<bool> NotifyDispatchedAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel an order: tell the shopper, and call off any delivery follow-up that has not yet gone
    /// out so it can never reach them. Returns false when the order does not exist.
    /// </summary>
    Task<bool> NotifyCancelledAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator re-send of a message that did not reach the shopper. Idempotent on
    /// <paramref name="idempotencyKey"/>: a repeat under the same key returns the message already
    /// produced without sending again; a fresh key sends a new message. Returns the (new or existing)
    /// notification. Returns null when there is no source notification with that id.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, keeping the fact it was sent and
    /// what became of it. Returns false if there is no such notification.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcile the provider's own record of messages from the configured sending number over a
    /// date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications produced for an order, each with its delivery outcome refreshed from the
    /// provider. Returns null when the order does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refresh from the provider, and return, every notification belonging to a shopper.</summary>
    Task<IReadOnlyList<OrderNotification>> RefreshOwnerNotificationsAsync(string ownerId, CancellationToken cancellationToken = default);
}
