using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>How a single message lined up between the provider's ledger and eShop's own records.</summary>
public enum ReconciliationOutcome
{
    /// <summary>Both the provider and eShop have a record of the message.</summary>
    Matched,

    /// <summary>The provider knows about the message but eShop has no record of sending it.</summary>
    ProviderOnly,

    /// <summary>eShop believes it sent the message but the provider's ledger for the range does not show it.</summary>
    EShopOnly
}

/// <summary>One message in the reconciliation, seen from whichever side(s) know about it.</summary>
public class ReconciliationEntry
{
    public ReconciliationEntry(
        string? sid,
        ReconciliationOutcome outcome,
        string? providerStatus,
        DateTimeOffset? providerDateSent,
        int? notificationId,
        int? orderId,
        string? eShopStatus)
    {
        Sid = sid;
        Outcome = outcome;
        ProviderStatus = providerStatus;
        ProviderDateSent = providerDateSent;
        NotificationId = notificationId;
        OrderId = orderId;
        EShopStatus = eShopStatus;
    }

    /// <summary>The provider message identifier the two sides are lined up on.</summary>
    public string? Sid { get; }

    public ReconciliationOutcome Outcome { get; }

    public string? ProviderStatus { get; }

    public DateTimeOffset? ProviderDateSent { get; }

    public int? NotificationId { get; }

    public int? OrderId { get; }

    public string? EShopStatus { get; }
}

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent, over a
/// date-time range, restricted to this application's configured sending number.
/// </summary>
public class NotificationReconciliationReport
{
    public NotificationReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        string fromNumber,
        IReadOnlyList<ReconciliationEntry> entries)
    {
        From = from;
        To = to;
        FromNumber = fromNumber;
        Entries = entries;
    }

    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    /// <summary>The sending number the provider's records were filtered to.</summary>
    public string FromNumber { get; }

    public IReadOnlyList<ReconciliationEntry> Entries { get; }

    public int TotalProviderMessages
    {
        get
        {
            var count = 0;
            foreach (var e in Entries)
            {
                if (e.Outcome != ReconciliationOutcome.EShopOnly) count++;
            }
            return count;
        }
    }

    public int MatchedCount => Count(ReconciliationOutcome.Matched);

    public int ProviderOnlyCount => Count(ReconciliationOutcome.ProviderOnly);

    public int EShopOnlyCount => Count(ReconciliationOutcome.EShopOnly);

    private int Count(ReconciliationOutcome outcome)
    {
        var count = 0;
        foreach (var e in Entries)
        {
            if (e.Outcome == outcome) count++;
        }
        return count;
    }
}
