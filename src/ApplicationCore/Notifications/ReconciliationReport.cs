using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The provider's own record of messages for a date range lined up against what eShop believes it sent.
/// A message the provider knows about that eShop does not (<see cref="ProviderOnly"/>), or the reverse
/// (<see cref="EShopOnly"/>), is made visible.
/// </summary>
public record ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Messages present in both the provider's record and eShop's.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider reports that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider's record for this range does not show.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}

/// <summary>
/// A single reconciled message. The destination number is deliberately omitted to avoid exposing shopper PII.
/// </summary>
public record ReconciliationEntry
{
    public string? MessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public int? OrderId { get; init; }
    public string? DateSent { get; init; }
}
