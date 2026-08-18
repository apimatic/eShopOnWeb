using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the text messages that go out as an order moves, and the operator actions on those
/// messages. A message that cannot be sent is recorded but never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed (one message per number they have on file).</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a follow-up with the provider for a few days
    /// later asking how the delivery went.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up that has not yet gone out, so
    /// nobody is asked how a cancelled delivery went.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message recorded as <paramref name="notificationId"/> to the same destination. The
    /// <paramref name="idempotencyKey"/> makes a repeat harmless: the same key returns the message the first
    /// call produced without sending again, a fresh key sends anew. Returns the resulting notification.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider (redaction) and locally, so the text is no longer
    /// retrievable anywhere, while the fact that the message was sent and what became of it survives.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the given notifications' delivery outcomes from the provider (best effort).</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any still-scheduled message queued to a number the shopper has just removed, so nothing is
    /// sent to it again.
    /// </summary>
    Task CancelScheduledForContactNumberAsync(string buyerId, string toNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines the provider's own record of messages sent from the configured sending number against what
    /// eShop believes it sent over the [<paramref name="fromUtc"/>, <paramref name="toUtc"/>] range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>The reconciliation of provider records against eShop's own over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderMessageCount,
    int EShopNotificationCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>A single line in a reconciliation report.</summary>
public record ReconciliationEntry(
    string? ProviderSid,
    string? ProviderStatus,
    DateTimeOffset? DateSent,
    int? NotificationId,
    int? OrderId,
    string? NotificationType);
