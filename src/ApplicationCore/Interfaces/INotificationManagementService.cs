using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendOutcome
{
    NotFound,
    Created,
    ReplayedIdempotent
}

/// <summary>Outcome of a resend: whether a new message was created or an earlier one replayed under the same key.</summary>
public record ResendResult(ResendOutcome Outcome, SmsNotification? Notification);

/// <summary>One line of the reconciliation report for a single message.</summary>
public record ReconciliationEntry(
    string ProviderMessageId,
    string? MaskedTo,
    string? ProviderStatus,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    int? NotificationId,
    string? EShopStatus);

/// <summary>
/// A reconciliation of the provider's record of sent messages against eShop's, over a date range.
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

/// <summary>Operator actions over individual notifications: re-send, dispose of content, and reconcile.</summary>
public interface INotificationManagementService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key makes a repeat of the
    /// same request a no-op that returns the message the first request produced; a fresh key sends anew.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. The fact of the message and its
    /// outcome survive. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's record of messages from the configured sending number over the range and
    /// lines them up against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
