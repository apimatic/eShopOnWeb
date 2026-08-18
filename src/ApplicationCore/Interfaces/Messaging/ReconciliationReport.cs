using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>Which ledger(s) a reconciliation line appears in.</summary>
public enum ReconciliationSource
{
    /// <summary>Present in both the provider's ledger and eShop's records.</summary>
    Matched = 0,
    /// <summary>The provider knows about this message but eShop has no record of it.</summary>
    ProviderOnly = 1,
    /// <summary>eShop believes it sent this message but the provider's ledger does not list it.</summary>
    EShopOnly = 2
}

/// <summary>One message lined up across the provider's ledger and eShop's records.</summary>
public record ReconciliationLine(
    string? ProviderMessageSid,
    ReconciliationSource Source,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId,
    DateTimeOffset? ProviderDateSent);

/// <summary>
/// A report over a date-time range comparing the provider's own record of messages sent from this
/// application's configured number against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
