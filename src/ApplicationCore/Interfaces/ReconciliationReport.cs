using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider's own record of messages for a date range, lined up against what eShop believes it
/// sent, so a message the provider knows about and eShop doesn't — or the reverse — is visible.
/// Counts only messages sent from the application's own configured sending number.
/// No shopper phone numbers appear in this report.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly)
{
    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;
}

/// <summary>
/// One line of a reconciliation report. <see cref="NotificationId"/> is present when eShop has a
/// record; <see cref="ProviderStatus"/> is present when the provider does.
/// </summary>
public record ReconciliationEntry(
    string? Sid,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus,
    string? ProviderStatus);
