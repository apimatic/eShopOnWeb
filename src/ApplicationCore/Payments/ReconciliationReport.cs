using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Whether a reconciliation line matched on both sides, or only one.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal and eShop both know about this payment.</summary>
    Matched,
    /// <summary>PayPal knows about a transaction that no eShop order claims.</summary>
    MissingInEShop,
    /// <summary>eShop has a payment that PayPal's report does not (yet) list.</summary>
    MissingInPayPal
}

/// <summary>One reconciled item: a PayPal transaction and/or an eShop order, and how they line up.</summary>
public record ReconciliationLine(
    ReconciliationMatch Match,
    string? InvoiceReference,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? CurrencyCode,
    decimal? EShopAmount,
    DateTimeOffset? TransactionDate);

/// <summary>
/// A reconciliation report over a date range: PayPal's own transaction records lined up against eShop
/// orders, so a payment one side knows about and the other does not is visible. Covers the whole range.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLine> Lines);
