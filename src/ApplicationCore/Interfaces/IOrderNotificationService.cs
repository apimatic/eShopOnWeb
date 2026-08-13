using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates the SMS messages that go out as an order moves. A message that cannot be sent never
/// fails the underlying order operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any delivery follow-up that has not
    /// yet gone out, so it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes the stored delivery outcome of a notification from the provider, when it has a
    /// provider identifier and is not already in a terminal state.
    /// </summary>
    Task RefreshDeliveryStateAsync(OrderNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// a repeat under the same key returns the message the first attempt produced without sending
    /// again; a fresh key sends a new message. Returns the notification the resend resolved to.
    /// </summary>
    Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Disposes of a message's content at the shopper's request: redacts the body at the provider so
    /// it can no longer be retrieved there, and clears it locally. The record that a message was sent
    /// and what became of it survives.
    /// </summary>
    Task DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken);
}
