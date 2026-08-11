using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a reconciliation line lines up between PayPal and eShop.</summary>
public enum ReconciliationMatch
{
    /// <summary>Both PayPal and eShop know about this payment.</summary>
    Matched = 0,

    /// <summary>PayPal has a transaction eShop has no order for.</summary>
    PayPalOnly = 1,

    /// <summary>eShop has a paid order PayPal's report has no transaction for (e.g. reporting lag).</summary>
    EShopOnly = 2
}

/// <summary>A single reconciled line pairing (where possible) a PayPal transaction with an eShop order.</summary>
public record ReconciliationLine(
    ReconciliationMatch Match,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalReferenceId,
    decimal? EShopAmount,
    decimal? PayPalAmount,
    string? Currency,
    string? Status);

/// <summary>The reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    int PayPalTransactionCount,
    int EShopPaidOrderCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
