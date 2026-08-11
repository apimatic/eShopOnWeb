using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a PayPal transaction and an eShop order line up (or fail to) in the reconciliation report.</summary>
public enum ReconciliationStatus
{
    /// <summary>PayPal and eShop agree: the transaction maps to a known eShop payment.</summary>
    Matched = 0,

    /// <summary>PayPal reports a transaction eShop has no record of.</summary>
    InPayPalOnly = 1,

    /// <summary>eShop captured a payment PayPal's report does not (yet) show.</summary>
    InEShopOnly = 2
}

/// <summary>A single reconciled row lining up PayPal's record against eShop's.</summary>
public record ReconciliationEntry(
    ReconciliationStatus Status,
    string? PayPalTransactionId,
    string? PayPalTransactionStatus,
    decimal? PayPalAmount,
    int? OrderId,
    string? EShopCaptureId,
    decimal? EShopCapturedAmount,
    string Currency,
    DateTimeOffset? Date,
    bool AmountsAgree);

/// <summary>The full reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCaptureCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
