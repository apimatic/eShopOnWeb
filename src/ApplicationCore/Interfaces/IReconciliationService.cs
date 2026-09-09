using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report that lines PayPal's own record of transactions up against eShop
/// orders for a date range, so a payment one side knows about and the other does not is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInPayPal)
{
    public int MatchedCount => Matched.Count;
    public int InPayPalNotInEShopCount => InPayPalNotInEShop.Count;
    public int InEShopNotInPayPalCount => InEShopNotInPayPal.Count;
}

/// <summary>
/// A single reconciliation line. Depending on which bucket it is in, one side may be null: a PayPal
/// transaction with no eShop order, or an eShop order/payment with no PayPal transaction.
/// </summary>
public record ReconciliationEntry(
    string? InvoiceId,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? EShopPaymentStatus,
    string? CurrencyCode);
