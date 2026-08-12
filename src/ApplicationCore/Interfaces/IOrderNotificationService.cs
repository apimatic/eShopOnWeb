using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and tracks the SMS notifications that go out as an order moves. Every send is best-effort:
/// a message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed (one message per number on file).</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a follow-up with the provider, a few days
    /// later, asking how the delivery went.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any not-yet-sent delivery follow-up so
    /// it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message a notification represents. Repeating the call under the same
    /// <paramref name="idempotencyKey"/> does not send a second message; a fresh key is a new send.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider (and marks it disposed locally) so the text is
    /// no longer retrievable, while the record that it was sent survives.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes each notification's delivery outcome from the provider's own record.</summary>
    Task RefreshDeliveryOutcomesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the provider's own record of messages from this application's sending number in a
    /// date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a resend. Exactly one of the flags is meaningful at a time.</summary>
public record ResendResult(OrderNotification? Notification, bool NotFound, bool AlreadyProcessed)
{
    public static ResendResult Missing() => new(null, true, false);
    public static ResendResult Replayed(OrderNotification existing) => new(existing, false, true);
    public static ResendResult Sent(OrderNotification produced) => new(produced, false, false);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopRecordCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<EShopOnlyRecord> EShopOnly);

/// <summary>A message both the provider and eShop know about, lined up.</summary>
public record ReconciliationMatch(
    string Sid,
    string ProviderStatus,
    int? ProviderErrorCode,
    int NotificationId,
    int OrderId,
    string Kind,
    string EShopStatus);

/// <summary>A message the provider knows about that eShop has no record of.</summary>
public record ProviderOnlyMessage(string Sid, string Status, int? ErrorCode, DateTimeOffset? DateSent);

/// <summary>A message eShop believes it sent that the provider's record does not show.</summary>
public record EShopOnlyRecord(int NotificationId, int OrderId, string Kind, string? Sid, string EShopStatus);
