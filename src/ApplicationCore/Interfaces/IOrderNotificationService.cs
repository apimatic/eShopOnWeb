using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves. Every "notify" call
/// is best-effort: a message that cannot be sent is recorded as such and never propagated
/// as a failure of the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a follow-up with the provider,
    /// to be sent a few days later, asking how the delivery went.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up that has not
    /// yet gone out, so it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message captured by <paramref name="notificationId"/>. Repeating the
    /// request under the same <paramref name="idempotencyKey"/> returns the notification the
    /// first attempt produced without sending again; a fresh key sends a new message.
    /// Returns the notification representing the (new or already-produced) re-send.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of the message content at the provider (redaction) and locally, while the
    /// fact that a message was sent and what became of it survives.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the stored delivery outcome of the supplied notifications from the provider,
    /// so a report reflects the provider's current view.
    /// </summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages sent from this application's configured
    /// sending number, over a date range, against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
