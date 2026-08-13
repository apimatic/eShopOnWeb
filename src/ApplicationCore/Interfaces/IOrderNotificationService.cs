using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Keeps shoppers informed by SMS as their orders move, and gives operators the levers to
/// recover from and account for what was sent. Sending a message must never fail the underlying
/// order operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a "how did delivery go?" follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out
    /// so it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. The <paramref name="idempotencyKey"/> makes a
    /// repeat under the same key return the same result instead of sending again. Returns null when the
    /// original notification does not exist.
    /// </summary>
    Task<SmsNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally. Returns null when the notification
    /// does not exist.
    /// </summary>
    Task<SmsNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refresh delivery outcomes from the provider for a set of notifications (used on read).</summary>
    Task RefreshDeliveryStateAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line up the provider's own record of messages sent from the configured number, over a date range,
    /// against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A message the provider and/or eShop know about, lined up by the provider's message SID.</summary>
public record ReconciliationEntry(
    string? MessageSid,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId,
    string Disposition);

/// <summary>The result of a reconciliation over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
