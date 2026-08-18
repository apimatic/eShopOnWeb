using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// One line of a reconciliation report: a message the provider knows about, one eShop believes it
/// sent, or one both agree on.
/// </summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus,
    string? MaskedTo,
    DateTimeOffset? DateSentUtc);

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent,
/// over a date range, for this application's configured sending number only.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>
/// Sends and tracks the SMS notifications that go out as an order moves, and gives operators the
/// tools to act on what actually reached the customer (re-send, content disposal, reconciliation).
/// The notify* methods never throw to their caller: a message that cannot be sent is recorded and
/// logged, but the underlying order operation still succeeds.
/// </summary>
public interface ISmsNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Notifications raised for an order, optionally refreshed against the provider first.</summary>
    Task<IReadOnlyList<SmsNotification>> GetOrderNotificationsAsync(int orderId, bool refresh = true, CancellationToken cancellationToken = default);

    /// <summary>Notifications for a set of orders, keyed by order id, optionally refreshed.</summary>
    Task<IReadOnlyDictionary<int, List<SmsNotification>>> GetNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refresh = true, CancellationToken cancellationToken = default);

    Task<SmsNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper, under a caller-supplied
    /// idempotency key. Repeating a request under the same key returns the message already produced
    /// rather than sending a second one. Returns null when the source notification does not exist.
    /// </summary>
    Task<SmsNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. The record of the message and
    /// its outcome survives. Returns null when the notification does not exist.
    /// </summary>
    Task<SmsNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
