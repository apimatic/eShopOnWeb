using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>One message as it appears when reconciling the provider's ledger against eShop's records.</summary>
public class ReconciliationEntry
{
    public required string MessageSid { get; init; }
    /// <summary>True if the provider's ledger contains this message.</summary>
    public bool InProvider { get; init; }
    /// <summary>True if eShop has a notification record for this message.</summary>
    public bool InEShop { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public DateTimeOffset? ProviderDateSent { get; init; }
    /// <summary>The eShop notification id, when eShop knows this message.</summary>
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
}

/// <summary>
/// A reconciliation of the provider's own record of messages sent from this application's configured
/// sending number, over a date range, against what eShop believes it sent. Discrepancies in either
/// direction are made visible.
/// </summary>
public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>Messages the provider knows about that eShop does not (other traffic, or a lost eShop record).</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider's ledger does not return for the range.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages present in both ledgers.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();
}
