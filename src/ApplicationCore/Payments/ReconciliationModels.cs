using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation report over a date range: PayPal's own record of transactions lined up against
/// eShop orders, so a payment PayPal knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationMatch> InPayPalOnly,
    IReadOnlyList<EShopPaymentRecord> InEShopOnly);

/// <summary>One PayPal transaction, with the eShop order it lined up to (null when unmatched).</summary>
public record ReconciliationMatch(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date,
    string? InvoiceId,
    int? OrderId);

/// <summary>
/// An eShop payment whose invoice PayPal's records did not report over the range. During a range
/// covering very recent activity this is expected (PayPal's reporting lags); over a settled range it
/// flags a genuine discrepancy.
/// </summary>
public record EShopPaymentRecord(
    int OrderId,
    string InvoiceId,
    string PayPalOrderId,
    string? CaptureId,
    decimal Amount,
    string Currency,
    string Status);
