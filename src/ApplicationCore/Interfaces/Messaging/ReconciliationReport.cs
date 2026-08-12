using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// Lines up the provider's own record of messages (for the configured sending number, over a date
/// range) against what eShop believes it sent, so a message the provider knows about and eShop
/// does not — or the reverse — is visible.
/// </summary>
public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>The sending number the provider was queried for.</summary>
    public required string FromNumber { get; init; }

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }

    /// <summary>Messages both the provider and eShop have a record of, keyed by SID.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider knows about but eShop has no notification for.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Notifications eShop believes it sent but the provider has no matching record for.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}

public record ReconciliationEntry
{
    public string? Sid { get; init; }

    /// <summary>The provider's status for the message, when the provider has a record.</summary>
    public string? ProviderStatus { get; init; }

    /// <summary>eShop's recorded status for the message, when eShop has a notification.</summary>
    public string? EShopStatus { get; init; }

    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? Kind { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
