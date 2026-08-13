using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the app's existing order/order-item model) and drives the SMS
/// notifications that go out as an order moves. A message that cannot be sent never fails the
/// underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the shopper from catalog items, then tell them it was placed.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, AddressData? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark an order dispatched. Tells the shopper it is on its way and queues a
    /// follow-up "how did delivery go?" message with the provider for a few days later. Returns
    /// false if the order does not exist.
    /// </summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel an order. Tells the shopper and calls off any follow-up that has not
    /// yet gone out so it never reaches them. Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderNotificationsView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What was sent for one of the caller's orders and what became of each message (statuses are
    /// refreshed from the provider). Reports NotFound if the order is not the caller's or does not exist.
    /// </summary>
    Task<OrderNotificationsResult> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. The idempotency key makes a
    /// repeated request under the same key a no-op (returns the message the first attempt produced),
    /// while a fresh key sends a genuine second message.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: dispose the content of a message at the provider (and locally), while the fact
    /// it was sent and what became of it survive.
    /// </summary>
    Task<DisposeContentOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: reconcile the provider's record of messages sent from the configured From
    /// number over a range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
