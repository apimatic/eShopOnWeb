using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Lines up the provider's own record of messages for a date range against what eShop believes it sent,
/// counting only messages from the application's configured sending number. A message the provider knows
/// about and eShop doesn't — or the reverse — is visible here.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>The sending number the provider was asked about (the application's own configured sender).</summary>
    public string FromNumber { get; init; } = default!;

    public int ProviderMessageCount { get; init; }
    public int EShopMessageCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }

    /// <summary>Messages both sides agree on (same provider identifier).</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages the provider reports but eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent but the provider did not return for the range.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = new List<ReconciliationEntry>();
}

/// <summary>
/// A single line in a <see cref="ReconciliationReport"/>. Destination numbers are masked; correlation is
/// by provider identifier.
/// </summary>
public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? MaskedTo { get; init; }
    public DateTimeOffset? DateSent { get; init; }

    // eShop side, when known:
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public NotificationKind? Kind { get; init; }
}
