using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One eShop order's payment lined up (or not) against PayPal's record.</summary>
public record ReconciliationLine(
    int OrderId,
    string InvoiceId,
    string? CaptureTransactionId,
    decimal EShopCapturedAmount,
    decimal? PayPalAmount,
    string? PayPalTransactionId,
    string? PayPalStatus);

/// <summary>A PayPal transaction that eShop has no matching captured order for.</summary>
public record UnmatchedPayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    decimal Amount,
    string Currency,
    string Status,
    string? EventCode,
    DateTimeOffset Date);

/// <summary>An eShop captured order that PayPal's report does not (yet) show.</summary>
public record UnmatchedEShopOrder(
    int OrderId,
    string InvoiceId,
    string? CaptureTransactionId,
    decimal CapturedAmount);

/// <summary>
/// Reconciliation report for a date range: what matched, what PayPal knows but eShop doesn't,
/// and what eShop captured but PayPal's report doesn't (yet) show.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCapturedOrderCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<UnmatchedPayPalTransaction> InPayPalNotInEShop,
    IReadOnlyList<UnmatchedEShopOrder> InEShopNotInPayPal);

public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to);
}
