using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and manages the SMS messages that go out as an order moves through its life. Every send is
/// best-effort: a message that cannot be sent is recorded as such but never fails the underlying
/// order operation, and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper their order is on its way and queues a "how did the delivery go?" follow-up
    /// with the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper their order was cancelled and calls off any delivery follow-up that has not
    /// yet gone out, so it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key makes repeats safe:
    /// repeating under the same key returns the message the first attempt produced without sending
    /// again; a fresh key sends a new one. Returns the resulting notification.
    /// </summary>
    Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the shopper's request — both here and at the provider — while
    /// the record that a message was sent and what became of it survives. Returns false when the
    /// notification does not exist.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the stored delivery outcome of an order's notifications from the provider (best-effort).</summary>
    Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the stored delivery outcome of all of a shopper's notifications from the provider (best-effort).</summary>
    Task RefreshBuyerNotificationStatusesAsync(string buyerId, CancellationToken cancellationToken = default);
}
