using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS messages that go out as an order moves, and the operator actions over them.
/// A message that cannot be sent never fails the underlying order operation; a shopper with no number
/// on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed. One message per registered number.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a "how did delivery go?" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out so
    /// it never reaches them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator re-send of a message that did not reach the shopper. Repeating under the same
    /// idempotency key returns the message the first call produced without sending a second one.
    /// Returns the resulting notification, or null if the source notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider (and mark it disposed locally). The record and
    /// its outcome survive. Returns false if the notification has no provider message to act on.
    /// </summary>
    Task<bool> DisposeContentAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh the given notifications' delivery outcomes from the provider (there is no inbound
    /// webhook, so current state is obtained by asking the provider). Persists any changes.
    /// </summary>
    Task RefreshDeliveryStatusesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Build a reconciliation report over a date range: the provider's own record of messages from the
    /// application's sending number, lined up against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A reconciliation of the provider's record against eShop's over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SenderNumber,
    int ProviderMessageCount,
    int EShopMessageCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderOnly> ProviderOnly,
    IReadOnlyList<ReconciliationEShopOnly> EShopOnly);

/// <summary>A message both sides agree on, matched by provider SID.</summary>
public record ReconciliationMatch(string MessageSid, int NotificationId, int OrderId, string EShopStatus, string ProviderStatus, int? ProviderErrorCode);

/// <summary>A message the provider knows about but eShop has no record of.</summary>
public record ReconciliationProviderOnly(string MessageSid, string ProviderStatus, int? ProviderErrorCode, DateTimeOffset? DateSent);

/// <summary>A message eShop believes it sent but the provider did not return for the range.</summary>
public record ReconciliationEShopOnly(string MessageSid, int NotificationId, int OrderId, string EShopStatus);
