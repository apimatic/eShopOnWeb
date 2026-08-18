using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS messages that go out as an order moves. Every method here is best-effort:
/// a message that cannot be sent must never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up that has not yet gone out.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Refreshes stored delivery status from the provider for the given notifications.</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// <paramref name="idempotencyKey"/> returns the message already produced rather than sending a
    /// second one.
    /// </summary>
    Task<ResendResult> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts the body at the provider so its text is no longer
    /// retrievable, while the fact it was sent and what became of it survive.
    /// </summary>
    Task DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>The result of a resend: the notification, and whether an existing one was returned unchanged.</summary>
public record ResendResult(OrderNotification Notification, bool Deduplicated);
