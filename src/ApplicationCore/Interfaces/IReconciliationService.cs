using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One reconciled line: a PayPal transaction lined up (or not) against an eShop order.</summary>
public record ReconciliationEntry(
    string? PayPalTransactionId,
    string? PayPalEventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopKind,       // "capture" | "refund" | "authorization" | "order"
    string? EShopStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotInPayPal);

/// <summary>
/// Produces a reconciliation report over a date range: PayPal's own transaction ledger lined up
/// against eShop's payment records, surfacing anything present on only one side.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
