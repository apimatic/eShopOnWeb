using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of a re-send request.</summary>
public record ResendOutcome(bool SourceFound, SmsNotification? Result, bool Replayed)
{
    public static ResendOutcome NotFound() => new(false, null, false);
}

/// <summary>One message lined up between the provider's records and eShop's own during reconciliation.</summary>
public record ReconciliationEntry(
    string Sid,
    bool InProvider,
    bool InEShop,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId);

/// <summary>
/// A reconciliation report over a date range: the provider's record of messages from this application's
/// configured sending number, lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>
/// The operator actions over notifications: re-send a message that did not reach the shopper, dispose of
/// a message's content at the provider, and reconcile provider records against eShop's own.
/// </summary>
public interface INotificationOperationsService
{
    /// <summary>
    /// Re-sends the message identified by <paramref name="notificationId"/> to its destination. Repeating a
    /// request under the same <paramref name="idempotencyKey"/> returns the earlier result without sending
    /// again; a fresh key is a genuine new attempt.
    /// </summary>
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of the message's content at the provider and locally, keeping the fact it was sent and its
    /// outcome. Returns false when the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
