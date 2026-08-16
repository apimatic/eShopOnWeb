using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A PayPal transaction lined up against an eShop order (either side may be absent).</summary>
public sealed record ReconciliationRow(
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    decimal? PayPalFee,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? OrderPaymentStatus,
    decimal? OrderTotal,
    string MatchState);

/// <summary>Full reconciliation report over a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalNotInEShopCount,
    int InEShopNotInPayPalCount,
    IReadOnlyList<ReconciliationRow> Rows);

/// <summary>
/// Builds a reconciliation report: PayPal's own transaction record for a range,
/// lined up against eShop orders so either-side gaps are visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
