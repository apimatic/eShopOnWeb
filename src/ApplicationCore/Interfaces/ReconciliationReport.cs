using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A reconciliation of the provider's own record of this application's messages against eShop's records,
/// over a date range. Every message either side knows about appears exactly once.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = new List<ReconciliationEntry>();

    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
}

public enum ReconciliationOutcome
{
    /// <summary>Both the provider and eShop have this message.</summary>
    Matched = 0,
    /// <summary>The provider knows about this message but eShop has no record of sending it.</summary>
    ProviderOnly = 1,
    /// <summary>eShop believes it sent this message but the provider's record for the range does not include it.</summary>
    EShopOnly = 2
}

public record ReconciliationEntry(
    string? MessageSid,
    ReconciliationOutcome Outcome,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId,
    DateTimeOffset? ProviderDateSent);
