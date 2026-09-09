using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One reconciled line: a PayPal transaction and/or the eShop order it lines up with.</summary>
public record ReconciliationLine(
    string MatchState,          // "Matched", "PayPalOnly", "EShopOnly"
    string? PayPalTransactionId,
    string? EventCode,
    string? TransactionStatus,
    decimal? PayPalAmount,
    string? Currency,
    int? OrderId,
    decimal? EShopAmount,
    string? EShopPaymentStatus,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? TransactionDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopOrderCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Builds a reconciliation report over a date range: PayPal's own transaction record lined up
/// against eShop orders, so a payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
