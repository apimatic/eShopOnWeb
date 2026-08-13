using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up the provider's own record of the messages sent from this application's configured
/// sending number against what eShop believes it sent, over a date range.
/// </summary>
public interface INotificationReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The result of a reconciliation over a range.</summary>
public class ReconciliationReport
{
    public ReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<ReconciliationEntry> matched,
        IReadOnlyList<ReconciliationEntry> atProviderOnly,
        IReadOnlyList<ReconciliationEntry> atEShopOnly)
    {
        From = from;
        To = to;
        Matched = matched;
        AtProviderOnly = atProviderOnly;
        AtEShopOnly = atEShopOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    /// <summary>Messages both the provider and eShop know about (agreement).</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; }

    /// <summary>Messages the provider recorded but eShop has no notification for.</summary>
    public IReadOnlyList<ReconciliationEntry> AtProviderOnly { get; }

    /// <summary>Notifications eShop believes it sent in the range but the provider's record does not show.</summary>
    public IReadOnlyList<ReconciliationEntry> AtEShopOnly { get; }
}

/// <summary>One line of a reconciliation report, keyed by the provider message identifier.</summary>
public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
