using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that go out as an order moves. A messaging failure here must
/// never fail the underlying order operation, and a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper their order is on its way, and queue a delivery follow-up with the provider for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper their order was cancelled, and call off any follow-up that has not yet gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator re-sends a message that did not reach the shopper. The idempotency key makes repeats safe:
    /// a repeat under the same key returns the message the first attempt produced without sending again.
    /// Returns the notification for the resend (new or, for a repeated key, the existing one).
    /// </summary>
    Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's content on the provider's side, keeping the fact and outcome of the message.</summary>
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refresh the stored delivery outcome of each non-terminal notification from the provider.</summary>
    Task RefreshStatusesAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line the provider's own record of this application's messages for a date range up against what eShop
    /// believes it sent, so a message one side knows about and the other doesn't is visible.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
