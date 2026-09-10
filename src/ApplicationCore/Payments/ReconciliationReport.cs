using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How an eShop order and a PayPal transaction line up during reconciliation.</summary>
public enum ReconciliationMatch
{
    /// <summary>Both PayPal and eShop know about this payment.</summary>
    Matched = 0,

    /// <summary>PayPal reported a transaction eShop has no order for.</summary>
    MissingInEShop = 1,

    /// <summary>eShop has an order whose payment PayPal's report does not (yet) show.</summary>
    MissingInPayPal = 2
}

/// <summary>One reconciled row: a PayPal transaction, an eShop order, or both, with any discrepancy flagged.</summary>
public record ReconciliationLine(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    string? PayPalReference,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? OrderStatus,
    decimal? EShopAmount,
    string? OrderReference,
    bool AmountMismatch);

/// <summary>
/// A reconciliation report over a date range: PayPal's own transactions lined up against eShop orders,
/// so a payment one side knows about and the other does not is visible. Covers the whole range.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    int PayPalTransactionCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLine> Lines);
