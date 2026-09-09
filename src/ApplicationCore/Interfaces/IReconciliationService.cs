using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One PayPal transaction lined up against the eShop order it belongs to (if any).</summary>
public record ReconciliationMatch(
    string PayPalTransactionId,
    string? Status,
    decimal Amount,
    string Currency,
    string? Reference,
    int? OrderId);

/// <summary>An eShop payment PayPal's report does not (yet) show for the requested range.</summary>
public record UnmatchedOrder(
    int OrderId,
    string Reference,
    decimal Amount,
    string Currency,
    string OrderStatus,
    DateTimeOffset? CapturedAt);

/// <summary>
/// A reconciliation report for a date range: PayPal transactions matched to eShop orders, plus the
/// two kinds of discrepancy — a payment PayPal knows about that eShop does not, and the reverse.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationMatch> InPayPalNotInEShop,
    IReadOnlyList<UnmatchedOrder> InEShopNotInPayPal,
    int PayPalTransactionCount);

public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
