using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up the provider's own record of messages (for this application's sending number, over a date
/// range) against what eShop believes it sent, so a message the provider knows about and eShop doesn't,
/// or the reverse, is visible.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderOnly> ProviderOnly,
    IReadOnlyList<ReconciliationEShopOnly> EShopOnly)
{
    public int ProviderCount => Matched.Count + ProviderOnly.Count;
    public int EShopCount => Matched.Count + EShopOnly.Count;
    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;
}

/// <summary>A message present in both records, keyed by the provider message SID.</summary>
public sealed record ReconciliationMatch(string Sid, int NotificationId, string? ProviderStatus, string? EShopStatus);

/// <summary>A message the provider has a record of that eShop has no notification for.</summary>
public sealed record ReconciliationProviderOnly(string Sid, string? ProviderStatus, DateTimeOffset? DateSent);

/// <summary>A message eShop believes it sent that the provider's record for this range does not contain.</summary>
public sealed record ReconciliationEShopOnly(string Sid, int NotificationId, string? EShopStatus, DateTimeOffset? SentAt);
