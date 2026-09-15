using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Lines the provider's own record of messages (for the configured sending number, over a date range)
/// up against what eShop believes it sent, so a message one side knows about and the other doesn't is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>The configured sending number the provider was queried for.</summary>
    public string FromNumber { get; init; } = string.Empty;

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider did not return.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages present on both sides, with each side's status.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();
}

public class ReconciliationEntry
{
    public string? Sid { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopState { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
