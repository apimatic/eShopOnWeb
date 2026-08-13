using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The result of lining up the provider's records against this application's for a date range.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly)
{
    public int ProviderMessageCount => Matched.Count + ProviderOnly.Count;
    public int EShopMessageCount => Matched.Count + EShopOnly.Count;
    public bool InSync => ProviderOnly.Count == 0 && EShopOnly.Count == 0;
}

/// <summary>
/// One line of a reconciliation report. Either side may be absent: a provider-only entry has no local
/// notification id, an eShop-only entry has no provider record for its stored identifier.
/// </summary>
public record ReconciliationEntry(
    string? ProviderSid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus);
