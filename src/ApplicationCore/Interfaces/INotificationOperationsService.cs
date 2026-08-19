using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator actions over notifications: resend, content disposal, reconciliation.</summary>
public interface INotificationOperationsService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on
    /// <paramref name="idempotencyKey"/>: repeating a request under the same key returns
    /// the message the first request produced instead of sending another. Returns null if
    /// the source notification does not exist.
    /// </summary>
    Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: the text is redacted at the provider and cleared
    /// locally, while the fact it was sent and its outcome survive. Returns false if the
    /// notification does not exist.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines the provider's own record of messages sent from the configured sending number,
    /// over the given range, up against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The reconciliation of provider records against eShop's own for a date range.</summary>
public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>The sending number the provider was asked about (Twilio:FromNumber).</summary>
    public required string FromNumber { get; init; }

    public int ProviderMessageCount { get; init; }
    public int EShopMessageCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>Messages the provider knows about (by SID) that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> OnlyAtProvider { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Notifications eShop believes it sent that the provider's record does not show.</summary>
    public IReadOnlyList<ReconciliationEntry> OnlyInEShop { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages present on both sides, with each side's status.</summary>
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = new List<ReconciliationMatch>();
}

public record ReconciliationEntry(string? MessageSid, string? Status, int? NotificationId);

public record ReconciliationMatch(string MessageSid, string ProviderStatus, string EShopStatus, int NotificationId);
