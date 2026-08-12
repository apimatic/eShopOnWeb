using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates the messages that go out as an order moves. Every method here treats messaging as
/// best-effort: a message that cannot be sent is recorded but never fails the caller's order operation,
/// and a shopper with no number on file is simply not messaged. Destination numbers are never logged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Returns the notifications created.</summary>
    Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any delivery follow-up that has not yet
    /// gone out, so a cancelled order never triggers a "how did delivery go?" message.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// idempotency key returns the earlier result without sending again; a fresh key sends a new message.
    /// Returns the notification representing the resend outcome.
    /// </summary>
    Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content: redacts the text at the provider so it is no longer retrievable
    /// there, then clears the local copy — while the fact it was sent and its outcome survive.
    /// </summary>
    Task DisposeContentAsync(OrderNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the last-known delivery outcome of the given notifications by asking the provider. Best
    /// effort — a notification whose state cannot be refreshed keeps the outcome already on record.
    /// </summary>
    Task RefreshStatusesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct = default);
}
