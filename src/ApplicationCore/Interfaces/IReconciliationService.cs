using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One PayPal transaction lined up against the eShop order it belongs to (if any).</summary>
public record ReconciliationRow(
    string PayPalTransactionId,
    string Status,
    string? EventCode,
    decimal Amount,
    decimal Fee,
    string Currency,
    DateTimeOffset Date,
    string? InvoiceId,
    int? MatchedOrderId,
    decimal? EShopAmount,
    bool AmountsAgree);

/// <summary>An eShop capture PayPal has not (yet) reported in the range.</summary>
public record MissingInPayPalRow(
    int OrderId,
    string InvoiceId,
    string? CaptureId,
    decimal CapturedAmount,
    string Currency,
    DateTimeOffset? CapturedAt);

/// <summary>
/// A reconciliation report over a date range: every PayPal transaction lined up against eShop orders,
/// plus eShop captures PayPal has not reported. Covers the whole range, not just the first page.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalNotInEShopCount,
    int InEShopNotInPayPalCount,
    IReadOnlyList<ReconciliationRow> Transactions,
    IReadOnlyList<MissingInPayPalRow> InEShopNotInPayPal,
    string Note);

public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
