using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// Lines the provider's own record of messages (for this application's configured sending number and a
/// date range) up against what eShop believes it sent, so a message one side knows about and the other
/// does not is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>The configured sending number the provider was queried for.</summary>
    public string FromNumber { get; init; } = string.Empty;

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }

    /// <summary>Messages both the provider and eShop agree on, keyed by SID.</summary>
    public List<ReconciliationEntry> Matched { get; init; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; init; } = new();

    /// <summary>Messages eShop believes it sent that the provider's list does not contain for this range.</summary>
    public List<ReconciliationEntry> EShopOnly { get; init; } = new();
}

public class ReconciliationEntry
{
    public string? Sid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? Kind { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public int? ErrorCode { get; init; }
}
