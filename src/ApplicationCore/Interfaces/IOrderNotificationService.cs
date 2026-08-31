using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates shopper notifications as orders move. Messaging failures never
/// fail the underlying order operation: they are recorded on the notification.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. No-op when no number is on file.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues the delivery follow-up
    /// with the provider for <paramref name="followUpDelay"/> later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, TimeSpan followUpDelay, CancellationToken ct = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up the
    /// provider has not sent yet.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the local view of the given notifications from the provider
    /// (only those not yet in a terminal state). Best effort: provider outages
    /// leave the last known state in place.
    /// </summary>
    Task RefreshAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeats under an
    /// idempotency key that already produced a resend return that resend without
    /// sending again.
    /// </summary>
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);
}

public enum ResendOutcome
{
    Resent = 0,
    DuplicateIdempotencyKey = 1,
    NotificationNotFound = 2,
    ContactNumberRemoved = 3,
    ContentRedacted = 4,
    ProviderRejected = 5
}

public sealed record ResendNotificationResult(ResendOutcome Outcome, OrderNotification? Notification, string? ErrorMessage);
