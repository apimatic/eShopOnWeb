using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How an eShop payment and a PayPal transaction line up (or fail to).</summary>
public enum ReconciliationMatch
{
    /// <summary>Both eShop and PayPal know about this payment.</summary>
    Matched,

    /// <summary>PayPal has a transaction that no eShop order references.</summary>
    InPayPalOnly,

    /// <summary>eShop has a captured payment that PayPal's report does not (yet) show.</summary>
    InEShopOnly
}

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    int? OrderId,
    string? PayPalTransactionId,
    string? CaptureId,
    decimal? EShopAmount,
    decimal? PayPalAmount,
    string? Currency,
    string? OrderStatus,
    string? PayPalStatus);

/// <summary>PayPal's transactions for a date range, lined up against eShop's captured payments.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCapturedPaymentCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
