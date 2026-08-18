using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-notification flows: placing orders, moving them through their lifecycle, and the
/// SMS messages that go out as they move. Messaging failures never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Registers a mobile number for a shopper. The number is validated with the provider and stored in its
    /// canonical form. Throws <see cref="Exceptions.SmsGatewayException"/> if it is not a usable destination,
    /// or if the provider could not be consulted.
    /// </summary>
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct = default);

    /// <summary>
    /// Places an order for the shopper from catalog items, reusing the existing order/order-item model, then
    /// tells the shopper it was placed. Returns the created order.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with the
    /// provider for a few days later asking how the delivery went.
    /// </summary>
    Task DispatchOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Marks the order cancelled, tells the shopper, and calls off any queued follow-up that has not yet gone
    /// out so it never reaches them.
    /// </summary>
    Task CancelOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper, under a caller-supplied idempotency key. Repeating a
    /// request under the same key returns the message the first attempt produced without sending again; a
    /// fresh key sends anew. Returns the notification the resend acted through.
    /// </summary>
    Task<OrderNotification> ResendAsync(OrderNotification notification, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content: redacts it at the provider so its text is no longer retrievable there,
    /// and clears it locally, while the fact it was sent and what became of it survive.
    /// </summary>
    Task DisposeContentAsync(OrderNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Refreshes a notification's delivery outcome from the provider's current view and persists it. A no-op
    /// for a notification the provider never accepted. Never throws for a messaging failure.
    /// </summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Builds a reconciliation report over a date range, comparing the provider's own record of messages from
    /// this application's sending number against eShop's records.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
