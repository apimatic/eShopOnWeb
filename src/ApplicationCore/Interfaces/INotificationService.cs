using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates the SMS messages that go out as an order moves, and the operator actions on them.
/// Sending a message must never fail the underlying order operation: these methods swallow provider
/// failures (recording them as message outcomes) except where an operator explicitly asks for a
/// provider-side effect (resend, content disposal, reconciliation).
/// </summary>
public interface INotificationService
{
    /// <summary>Tell the shopper their order was placed. No-op if they have no number on file.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way and queue a "how did delivery go?" follow-up with the
    /// provider for a few days later. No-op if they have no number on file.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled and call off any not-yet-sent delivery follow-up so it
    /// never reaches them. No-op if they have no number on file.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Repeats under the same idempotency key are not
    /// re-sent; a fresh key is a legitimate new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider (and here). The fact that a message was sent, and
    /// what became of it, survives. Throws if the provider cannot dispose of the content.
    /// </summary>
    Task DisposeContentAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>Refresh the delivery outcome of the given notifications from the provider's current view.</summary>
    Task RefreshDeliveryStateAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default);

    /// <summary>Reconcile the provider's record of messages from our sending number against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
