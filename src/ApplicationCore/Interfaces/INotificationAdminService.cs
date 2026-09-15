using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator actions over individual notifications: re-sending a message that did not reach the
/// shopper, disposing of a message's content, and reconciling this app's record against the provider's.
/// </summary>
public interface INotificationAdminService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on the caller-supplied key:
    /// repeating a request under the same key returns the message the first request produced without
    /// sending a second; a fresh key sends again. Returns null when the notification does not exist.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. Returns false when the notification
    /// does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages this app sent within a range and lines them up
    /// against what this app believes it sent, counting only messages sent from the app's configured
    /// sending number.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of a resend: the notification produced and whether it was a replay of an earlier key.</summary>
public record ResendResult(Notification Notification, bool WasReplay);

/// <summary>
/// A reconciliation report over a date range: what both sides agree on, what only the provider knows,
/// and what only this app knows.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report, keyed by the provider message identifier.</summary>
public record ReconciliationEntry(
    string ProviderMessageId,
    string? Status,
    int? NotificationId,
    int? OrderId,
    DateTimeOffset? DateSent);
