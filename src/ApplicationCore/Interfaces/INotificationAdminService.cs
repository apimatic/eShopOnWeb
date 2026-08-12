using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendOutcome
{
    Sent = 0,
    ReplayedIdempotent = 1,
    NotFound = 2,
    Invalid = 3
}

/// <summary>Result of an operator resend. <see cref="NotificationId"/> is the message the resend produced.</summary>
public record ResendResult(ResendOutcome Outcome, int NotificationId, string? Status, string? Error);

public enum ContentDisposalOutcome
{
    Disposed = 0,
    NotFound = 1,
    ProviderFailed = 2
}

/// <summary>Result of disposing of a message's content.</summary>
public record ContentDisposalResult(ContentDisposalOutcome Outcome, string? Error);

/// <summary>Operator actions on individual notifications: resend, content disposal, reconciliation.</summary>
public interface INotificationAdminService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// idempotency key returns the message the first request produced without sending again; a fresh
    /// key is a genuine second attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts it at the provider so its text is no longer retrievable
    /// there and clears the local copy, while the record that it was sent survives.
    /// </summary>
    Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines the provider's own record of messages from the configured sending number up against what
    /// eShop believes it sent, over a date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
