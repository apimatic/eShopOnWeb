using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's own record of messages for a date range, lined up against what eShop believes it
/// sent, so a message the provider knows about and eShop doesn't — or the reverse — is visible.
/// Deliberately excludes destination numbers (PII).
/// </summary>
public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    // Messages present in both the provider's record and eShop's.
    public List<ReconciliationEntry> Matched { get; init; } = new();

    // Present at the provider (sent from our number in range) but not recorded by eShop.
    public List<ReconciliationEntry> ProviderOnly { get; init; } = new();

    // Recorded by eShop (with a provider identifier, in range) but absent from the provider's record.
    public List<ReconciliationEntry> EShopOnly { get; init; } = new();

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }
}

public class ReconciliationEntry
{
    public string? Sid { get; init; }
    public string? ProviderStatus { get; init; }
    public DateTimeOffset? DateSent { get; init; }

    // eShop's local view, when known.
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? RecordedStatus { get; init; }
}
