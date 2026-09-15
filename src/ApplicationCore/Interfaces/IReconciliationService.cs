using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of the reconciliation report, seen from PayPal, from eShop, or from both.</summary>
public record ReconciliationEntry(
    string? EShopOrderId,
    string? PayPalTransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? Date,
    string Note);

/// <summary>
/// Lines up PayPal's own record of transactions for a date range against eShop orders so that a payment
/// PayPal knows about and eShop doesn't — or the reverse — is visible. Covers the whole range.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopOrderCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> MissingInEShop,
    IReadOnlyList<ReconciliationEntry> MissingInPayPal);

public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
