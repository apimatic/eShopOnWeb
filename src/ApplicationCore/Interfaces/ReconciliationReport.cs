using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A reconciliation of the provider's own record of messages (from this application's sending number, over a
/// date range) against what eShop believes it sent. A message the provider knows about and eShop does not —
/// or the reverse — shows up in <see cref="ProviderOnly"/> / <see cref="EShopOnly"/>.
/// </summary>
public class ReconciliationReport
{
    public ReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        string fromNumber,
        IReadOnlyList<ReconciliationEntry> matched,
        IReadOnlyList<ReconciliationEntry> providerOnly,
        IReadOnlyList<ReconciliationEntry> eShopOnly)
    {
        From = from;
        To = to;
        FromNumber = fromNumber;
        Matched = matched;
        ProviderOnly = providerOnly;
        EShopOnly = eShopOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    /// <summary>The sending number the provider was queried for.</summary>
    public string FromNumber { get; }

    /// <summary>Messages both the provider and eShop know about, by SID.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; }

    /// <summary>Messages the provider knows about but eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; }

    /// <summary>Messages eShop recorded sending but the provider did not return.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; }

    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;
}

/// <summary>One line of a <see cref="ReconciliationReport"/>.</summary>
public class ReconciliationEntry
{
    public ReconciliationEntry(
        string sid,
        string? providerStatus,
        string? eShopStatus,
        int? orderId,
        DateTimeOffset? dateSent)
    {
        Sid = sid;
        ProviderStatus = providerStatus;
        EShopStatus = eShopStatus;
        OrderId = orderId;
        DateSent = dateSent;
    }

    public string Sid { get; }

    /// <summary>The provider's status for this message, when the provider returned it.</summary>
    public string? ProviderStatus { get; }

    /// <summary>eShop's recorded status for this message, when eShop has a record.</summary>
    public string? EShopStatus { get; }

    /// <summary>The eShop order this message is about, when eShop has a record.</summary>
    public int? OrderId { get; }

    public DateTimeOffset? DateSent { get; }
}
