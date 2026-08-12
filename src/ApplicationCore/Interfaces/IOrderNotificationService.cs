using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order together with the notifications raised for it (and their current delivery outcome).</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>
/// Orchestrates the messages that go out as an order moves. A message that cannot be sent never
/// fails the underlying operation: the order is still placed/dispatched/cancelled and the caller's
/// request still succeeds. A shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for the shopper from catalog item ids + quantities, reusing the existing
    /// order/order-item model, then tell the shopper it was placed. Returns the created order, or
    /// <see cref="ResultStatus.Invalid"/> if the request has no valid lines / unknown catalog items.
    /// </summary>
    Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark an order dispatched, tell the shopper it is on its way, and queue a
    /// "how did delivery go?" follow-up with the provider for a few days later.
    /// </summary>
    Task<Result> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel an order, tell the shopper, and call off any follow-up that has not
    /// yet gone out so it never reaches them.
    /// </summary>
    Task<Result> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own orders, each with the notifications raised for it (delivery outcomes refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order, with each message's current provider outcome refreshed.
    /// Shopper-scoped: <see cref="ResultStatus.NotFound"/> unless the order belongs to the caller.
    /// </summary>
    Task<Result<IReadOnlyList<OrderNotification>>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. Repeating under the same
    /// idempotency key returns the already-produced message without sending again; a fresh key sends
    /// a new one. Returns the notification the resend produced.
    /// </summary>
    Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: dispose of a message's content at the shopper's request. The text is redacted
    /// at the provider and cleared here; the fact a message was sent, and what became of it, survives.
    /// </summary>
    Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: reconcile the provider's record of messages for this application's sending
    /// number over [from, to] against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
