using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Flow 3 — operator actions over notifications: resend, content disposal, reconciliation.</summary>
public interface INotificationAdminService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on the caller-supplied key: repeating a
    /// request under the same key returns the first result without sending again; a fresh key sends anew.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Disposes of a message's content at the provider (so its text is no longer retrievable there) while the
    /// fact it was sent and its outcome survive.
    /// </summary>
    Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>
    /// Lines up the provider's own record of messages sent from the configured sending number over a date
    /// range against what the shop believes it sent, so gaps on either side are visible. Covers the whole
    /// range (paginated).
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public enum ResendOutcome
{
    NotFound,
    Sent,
    DuplicateIgnored,
    NothingToResend,
    ProviderUnavailable
}

public record ResendResult(ResendOutcome Outcome, int? NotificationId, string? Message);

public enum ContentDisposalOutcome
{
    NotFound,
    Disposed,
    AlreadyDisposed,
    NothingToDispose,
    ProviderUnavailable
}

public record ContentDisposalResult(ContentDisposalOutcome Outcome, string? Message);

/// <summary>One message lined up across the two sources.</summary>
public record ReconciliationEntry(string? ProviderMessageSid, string? ProviderStatus,
    DateTimeOffset? ProviderSentAt, int? NotificationId, int? OrderId, string? LocalStatus);

/// <summary>
/// The reconciliation result. <see cref="Truncated"/> is set (with <see cref="PagesRead"/>) if a safety page
/// cap stopped the walk before the provider signalled the end, so the caller learns the answer was cut short.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly,
    bool Truncated,
    int PagesRead);
