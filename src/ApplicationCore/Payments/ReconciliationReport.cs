using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A PayPal transaction that lines up with an eShop capture or refund.</summary>
public record MatchedTransaction(
    string TransactionId,
    int OrderId,
    string Kind,            // "Capture" or "Refund"
    string PayPalStatus,
    decimal PayPalAmount,
    decimal EShopAmount,
    bool AmountsAgree);

/// <summary>A transaction PayPal knows about that eShop cannot account for.</summary>
public record PayPalOnlyTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? Date);

/// <summary>An eShop capture/refund that PayPal's report does not (yet) show.</summary>
public record EShopOnlyEntry(
    int OrderId,
    string Kind,            // "Capture" or "Refund"
    string Reference,       // capture id or refund id
    decimal Amount,
    string Status);

/// <summary>
/// A reconciliation report over a date range: PayPal's own transaction record lined up against eShop
/// orders, surfacing mismatches in either direction. Covers the whole range (all pages).
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalPagesRead,
    int PayPalTransactionCount,
    IReadOnlyList<MatchedTransaction> Matched,
    IReadOnlyList<PayPalOnlyTransaction> PayPalOnly,
    IReadOnlyList<EShopOnlyEntry> EShopOnly);
