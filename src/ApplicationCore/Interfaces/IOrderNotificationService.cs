using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders using the app's existing order/order-item model and keeps the shopper informed by
/// SMS as the order moves. A message that cannot be sent never fails the underlying operation; a
/// shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from catalog item ids and quantities, then
    /// tells the shopper it was placed. Returns the new order id.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress);

    /// <summary>
    /// Marks an order dispatched, tells the shopper it is on its way, and queues a follow-up with
    /// the provider for a few days later asking how the delivery went.
    /// </summary>
    Task<OrderActionResult> DispatchOrderAsync(int orderId);

    /// <summary>
    /// Cancels an order, tells the shopper, and calls off any follow-up that has not yet gone out.
    /// </summary>
    Task<OrderActionResult> CancelOrderAsync(int orderId);

    /// <summary>The caller's orders, each with where its notifications got to.</summary>
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId);

    /// <summary>
    /// The notifications sent for one order and what became of each. Scoped to the order owner
    /// unless <paramref name="isAdmin"/> is true.
    /// </summary>
    Task<OrderNotificationsResult> GetOrderNotificationsAsync(int orderId, string callerId, bool isAdmin);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. A repeat request under the same
    /// idempotency key does not send again; a fresh key is a genuine second attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey);

    /// <summary>
    /// Disposes of a message's content at the provider (and locally) while keeping the fact it was
    /// sent and what became of it.
    /// </summary>
    Task<DisposeContentResult> DisposeContentAsync(int notificationId);

    /// <summary>
    /// Reconciles the provider's own record of messages sent from the configured sending number in
    /// [<paramref name="from"/>, <paramref name="to"/>] against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
