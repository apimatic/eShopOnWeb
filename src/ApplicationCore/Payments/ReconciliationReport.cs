using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation report over a date range: PayPal's own transaction records lined up against
/// eShop orders. Mismatches in either direction are flagged so an operator can act on them.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>How a transaction and/or an order line up during reconciliation.</summary>
public enum ReconciliationOutcome
{
    /// <summary>PayPal and eShop agree: a transaction maps to a known order.</summary>
    Matched = 0,

    /// <summary>PayPal knows about a transaction eShop cannot map to an order.</summary>
    InPayPalOnly = 1,

    /// <summary>eShop has a captured order PayPal's report does not (yet) list.</summary>
    InEShopOnly = 2
}

/// <summary>One reconciled row.</summary>
public record ReconciliationLine(
    ReconciliationOutcome Outcome,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? TransactionDate,
    int? OrderId,
    decimal? OrderCapturedAmount,
    string? CaptureId);
