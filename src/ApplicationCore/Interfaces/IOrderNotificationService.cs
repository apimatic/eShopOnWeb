using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates order placement and the SMS notifications that go out as an order moves.
/// A message that cannot be sent never fails the underlying operation - the order is still
/// placed, dispatched or cancelled - and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog items for the given shopper and tells them it was placed. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderItemInput> items, ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks an order dispatched, tells the shopper it is on its way, and queues a delivery follow-up with the provider for a few days later.</summary>
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels an order, tells the shopper, and calls off any not-yet-sent follow-up so it never reaches them.</summary>
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>The notifications for one of the caller's own orders. Another shopper's order is not visible.</summary>
    Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: re-sends a message that did not reach the shopper. Repeating the request under
    /// the same idempotency key returns the same message without sending again; a fresh key sends anew.
    /// Returns the notificationId of the message the resend produced.
    /// </summary>
    Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Operator action: disposes of a message's content at the provider and here, keeping the fact and outcome.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles the provider's record of messages from the configured number against eShop's for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
